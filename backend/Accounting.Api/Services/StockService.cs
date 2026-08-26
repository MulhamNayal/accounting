using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface IStockService
{
    Task<ItemSummary> CreateItemAsync(CreateItemRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ItemSummary>> ListItemsAsync(CancellationToken ct = default);

    Task<StockMoveSummary> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default);

    Task<StockIssueResult> IssueAsync(IssueStockRequest request, CancellationToken ct = default);

    Task<CostAdjustmentResult> AdjustCostAsync(AdjustCostRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<StockOnHand>> GetOnHandAsync(Guid legalEntityId, CancellationToken ct = default);

    Task<IReadOnlyList<CostLayerDetail>> GetLayersAsync(
        Guid legalEntityId, Guid itemId, CancellationToken ct = default);

    Task<IReadOnlyList<StockMoveSummary>> GetMovesAsync(
        Guid legalEntityId, Guid? itemId, CancellationToken ct = default);
}

/// <summary>
/// Stock movements and FIFO cost.
/// </summary>
/// <remarks>
/// Nothing about quantity or value is stored on the item. Quantity comes from the moves and
/// value from the layers that have not been fully consumed, so stock on hand and the
/// inventory account are two summaries of the same rows rather than two records that must
/// be kept in step.
/// </remarks>
public sealed class StockService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    IPostingService postings,
    ILogger<StockService> logger) : IStockService
{
    // ---------------------------------------------------------------- items

    public async Task<ItemSummary> CreateItemAsync(
        CreateItemRequest request, CancellationToken ct = default)
    {
        var inventory = await RequireAccountAsync(request.InventoryAccountId, ct);
        var cogs = await RequireAccountAsync(request.CostOfSalesAccountId, ct);

        if (inventory.ControlType != ControlType.Stock)
        {
            throw new PostingValidationException(
                $"Account {inventory.Code} ({inventory.Name}) is not a stock control account. "
                + "Stock value has to sit somewhere the subledger can be reconciled against.");
        }

        if (cogs.AccountType != AccountType.Expense)
        {
            throw new PostingValidationException(
                $"Account {cogs.Code} ({cogs.Name}) is not an expense account, so it cannot "
                + "hold cost of sales.");
        }

        var tenantId = inventory.TenantId;

        if (await db.Items.AnyAsync(i => i.TenantId == tenantId && i.Code == request.Code, ct))
        {
            throw new PostingValidationException($"An item with code {request.Code} already exists.");
        }

        var item = new Item
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            BaseUom = request.BaseUom,
            InventoryAccountId = inventory.Id,
            CostOfSalesAccountId = cogs.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        return Summarise(item);
    }

    public async Task<IReadOnlyList<ItemSummary>> ListItemsAsync(CancellationToken ct = default)
        => await db.Items
            .AsNoTracking()
            .OrderBy(i => i.Code)
            .Select(i => new ItemSummary(
                i.Id, i.Code, i.Name, i.BaseUom,
                i.InventoryAccountId, i.CostOfSalesAccountId, i.IsActive))
            .ToListAsync(ct);

    // ---------------------------------------------------------------- receipts

    /// <summary>
    /// Brings stock in at a known cost, creating the layer future issues will consume.
    /// </summary>
    public async Task<StockMoveSummary> ReceiveAsync(
        ReceiveStockRequest request, CancellationToken ct = default)
    {
        var userId = RequireUser();

        if (request.Quantity <= 0)
        {
            throw new PostingValidationException(
                "A receipt must be for a positive quantity. Returning stock to a supplier is "
                + "an issue, not a negative receipt.");
        }

        if (request.UnitCost <= 0)
        {
            throw new PostingValidationException(
                "A receipt needs a positive unit cost. Stock with no cost basis makes every "
                + "later cost of sales wrong.");
        }

        var item = await RequireItemAsync(request.ItemId, ct);
        await RequireAccountAsync(request.CreditAccountId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var move = new StockMove
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            LegalEntityId = request.LegalEntityId,
            ItemId = item.Id,
            Direction = StockDirection.In,
            Quantity = request.Quantity,
            MovedOn = request.MovedOn,
            SourceDocumentType = "StockReceipt",
            PostedAtUtc = DateTimeOffset.UtcNow,
            PostedByUserId = userId,
            Description = request.Description,
        };

        var value = decimal.Round(request.Quantity * request.UnitCost, 4, MidpointRounding.ToEven);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                request.LegalEntityId,
                request.MovedOn,
                [
                    new PostingLineRequest(
                        item.InventoryAccountId, nameof(PostingDirection.Debit), value,
                        ItemId: item.Id,
                        Description: request.Description ?? $"Received {item.Code}"),
                    new PostingLineRequest(
                        request.CreditAccountId, nameof(PostingDirection.Credit), value,
                        SupplierId: request.SupplierId,
                        Description: request.Description ?? $"Received {item.Code}"),
                ],
                Memo: $"Stock receipt: {request.Quantity} {item.BaseUom} of {item.Code}",
                SourceDocumentType: "StockReceipt",
                SourceDocumentId: move.Id),
            ct);

        move.JournalEntryId = entry.Id;
        db.StockMoves.Add(move);

        db.CostLayers.Add(new CostLayer
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            LegalEntityId = request.LegalEntityId,
            ItemId = item.Id,
            SourceMoveId = move.Id,
            QuantityReceived = request.Quantity,
            UnitCost = request.UnitCost,
            ReceivedOn = request.MovedOn,
            Sequence = await NextSequenceAsync(item.Id, ct),
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new StockMoveSummary(
            move.Id, item.Id, item.Code, item.Name, move.Direction.ToString(),
            move.Quantity, move.MovedOn, move.SourceDocumentType, move.JournalEntryId,
            move.Description);
    }

    // ---------------------------------------------------------------- issues

    /// <summary>
    /// Takes stock out, costing it from the oldest layers with quantity remaining.
    /// </summary>
    /// <remarks>
    /// Cost of sales is the sum of what was actually consumed, not quantity times an average.
    /// That is what makes it explainable: for any issue, the receipts its cost came from can
    /// be named.
    /// </remarks>
    public async Task<StockIssueResult> IssueAsync(
        IssueStockRequest request, CancellationToken ct = default)
    {
        var userId = RequireUser();

        if (request.Quantity <= 0)
        {
            throw new PostingValidationException("An issue must be for a positive quantity.");
        }

        var item = await RequireItemAsync(request.ItemId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var layers = await LayersWithRemainingAsync(request.LegalEntityId, item.Id, ct);
        var available = layers.Sum(l => l.Remaining);

        if (available < request.Quantity)
        {
            throw new PostingValidationException(
                $"{item.Code} has {available} {item.BaseUom} on hand but {request.Quantity} "
                + "was issued. Issuing stock that does not exist would give it a cost basis "
                + "of nothing and understate cost of sales.");
        }

        var move = new StockMove
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            LegalEntityId = request.LegalEntityId,
            ItemId = item.Id,
            Direction = StockDirection.Out,
            Quantity = request.Quantity,
            MovedOn = request.MovedOn,
            SourceDocumentType = "StockIssue",
            PostedAtUtc = DateTimeOffset.UtcNow,
            PostedByUserId = userId,
            Description = request.Description,
        };

        // Oldest first. Sequence rather than date, so same-day receipts still have an order.
        var outstanding = request.Quantity;
        var consumptions = new List<CostConsumption>();

        foreach (var layer in layers.OrderBy(l => l.Layer.Sequence))
        {
            if (outstanding <= 0)
            {
                break;
            }

            var take = Math.Min(outstanding, layer.Remaining);
            if (take <= 0)
            {
                continue;
            }

            consumptions.Add(new CostConsumption
            {
                Id = Guid.NewGuid(),
                TenantId = item.TenantId,
                CostLayerId = layer.Layer.Id,
                OutMoveId = move.Id,
                Quantity = take,
                UnitCost = layer.Layer.UnitCost,
                Amount = decimal.Round(take * layer.Layer.UnitCost, 4, MidpointRounding.ToEven),
            });

            outstanding -= take;
        }

        var totalCost = consumptions.Sum(c => c.Amount);

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                request.LegalEntityId,
                request.MovedOn,
                [
                    new PostingLineRequest(
                        item.CostOfSalesAccountId, nameof(PostingDirection.Debit), totalCost,
                        CustomerId: request.CustomerId,
                        ItemId: item.Id,
                        Description: request.Description ?? $"Cost of {item.Code}"),
                    new PostingLineRequest(
                        item.InventoryAccountId, nameof(PostingDirection.Credit), totalCost,
                        ItemId: item.Id,
                        Description: request.Description ?? $"Issued {item.Code}"),
                ],
                Memo: $"Stock issue: {request.Quantity} {item.BaseUom} of {item.Code}",
                SourceDocumentType: "StockIssue",
                SourceDocumentId: move.Id),
            ct);

        move.JournalEntryId = entry.Id;
        db.StockMoves.Add(move);
        db.CostConsumptions.AddRange(consumptions);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Issued {Quantity} of {Item} at a cost of {Cost} across {Layers} layer(s)",
            request.Quantity, item.Code, totalCost, consumptions.Count);

        var layerSequences = layers.ToDictionary(l => l.Layer.Id, l => l.Layer.Sequence);

        return new StockIssueResult(
            move.Id,
            entry.Id,
            request.Quantity,
            totalCost,
            consumptions
                .Select(c => new ConsumptionDetail(
                    c.CostLayerId, layerSequences[c.CostLayerId], c.Quantity, c.UnitCost, c.Amount))
                .ToList());
    }

    // ---------------------------------------------------------------- cost adjustment

    /// <summary>
    /// Corrects the cost of a receipt already made, without rewriting history.
    /// </summary>
    /// <remarks>
    /// The difference splits by where the stock now is. Whatever is still on hand is worth
    /// more or less than recorded, so that share adjusts inventory. Whatever has already
    /// been sold was costed wrong, so that share adjusts cost of sales — posted into the
    /// current period, never by restating the original entry. Prior figures stand as
    /// reported and the correction is visible as a correction, which is what an auditor
    /// needs and what an in-place recompute cannot provide.
    /// </remarks>
    public async Task<CostAdjustmentResult> AdjustCostAsync(
        AdjustCostRequest request, CancellationToken ct = default)
    {
        var userId = RequireUser();

        if (request.CorrectedUnitCost <= 0)
        {
            throw new PostingValidationException("A corrected cost must be positive.");
        }

        var layer = await db.CostLayers
            .Include(l => l.Item)
            .FirstOrDefaultAsync(l => l.Id == request.CostLayerId, ct)
            ?? throw new NotFoundException($"No cost layer with id {request.CostLayerId}.");

        if (layer.AdjustsLayerId is not null)
        {
            throw new PostingValidationException(
                "That layer is itself an adjustment. Adjust the original receipt.");
        }

        var difference = decimal.Round(
            request.CorrectedUnitCost - layer.UnitCost, 4, MidpointRounding.ToEven);

        if (difference == 0)
        {
            throw new PostingValidationException(
                $"The cost is already {layer.UnitCost}. Nothing to correct.");
        }

        var item = layer.Item!;
        await RequireAccountAsync(request.CounterAccountId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var consumed = await db.CostConsumptions
            .Where(c => c.CostLayerId == layer.Id)
            .SumAsync(c => (decimal?)c.Quantity, ct) ?? 0m;

        var remaining = layer.QuantityReceived - consumed;

        var inventoryAdjustment = decimal.Round(remaining * difference, 4, MidpointRounding.ToEven);
        var cogsAdjustment = decimal.Round(consumed * difference, 4, MidpointRounding.ToEven);
        var total = inventoryAdjustment + cogsAdjustment;

        var lines = new List<PostingLineRequest>();

        // A cost increase debits what it affects and credits the counter account; a decrease
        // is the mirror. Amounts are always positive - direction carries the sign.
        var side = difference > 0
            ? nameof(PostingDirection.Debit)
            : nameof(PostingDirection.Credit);
        var counterSide = difference > 0
            ? nameof(PostingDirection.Credit)
            : nameof(PostingDirection.Debit);

        if (inventoryAdjustment != 0)
        {
            lines.Add(new PostingLineRequest(
                item.InventoryAccountId, side, Math.Abs(inventoryAdjustment),
                ItemId: item.Id,
                Description: $"Cost correction on stock still held: {item.Code}"));
        }

        if (cogsAdjustment != 0)
        {
            lines.Add(new PostingLineRequest(
                item.CostOfSalesAccountId, side, Math.Abs(cogsAdjustment),
                ItemId: item.Id,
                Description: $"Cost correction on stock already sold: {item.Code}"));
        }

        lines.Add(new PostingLineRequest(
            request.CounterAccountId, counterSide, Math.Abs(total),
            Description: request.Reason ?? $"Cost correction on {item.Code}"));

        var entry = await postings.PostAsync(
            new PostJournalEntryRequest(
                request.LegalEntityId,
                request.AdjustedOn,
                lines,
                Memo: $"Cost correction on {item.Code}: {layer.UnitCost} to {request.CorrectedUnitCost}",
                SourceDocumentType: "StockCostAdjustment",
                SourceDocumentId: layer.Id),
            ct);

        // A new layer, not an edit. The original's consumptions posted at the cost that was
        // true when they happened; this records the revised basis for what is still on hand,
        // so future issues consume at the corrected cost.
        var replacement = new CostLayer
        {
            Id = Guid.NewGuid(),
            TenantId = layer.TenantId,
            LegalEntityId = layer.LegalEntityId,
            ItemId = layer.ItemId,
            SourceMoveId = layer.SourceMoveId,
            QuantityReceived = 0m,
            UnitCost = request.CorrectedUnitCost,
            ReceivedOn = request.AdjustedOn,
            Sequence = await NextSequenceAsync(layer.ItemId, ct),
            AdjustsLayerId = layer.Id,
        };

        db.CostLayers.Add(replacement);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Cost correction on {Item}: {Difference} per unit, {Inventory} to inventory and {Cogs} to cost of sales",
            item.Code, difference, inventoryAdjustment, cogsAdjustment);

        return new CostAdjustmentResult(
            replacement.Id, entry.Id, difference, remaining, consumed,
            inventoryAdjustment, cogsAdjustment);
    }

    // ---------------------------------------------------------------- reporting

    public async Task<IReadOnlyList<StockOnHand>> GetOnHandAsync(
        Guid legalEntityId, CancellationToken ct = default)
    {
        var items = await db.Items.AsNoTracking().OrderBy(i => i.Code).ToListAsync(ct);
        var result = new List<StockOnHand>();

        foreach (var item in items)
        {
            var layers = await LayersWithRemainingAsync(legalEntityId, item.Id, ct);
            var quantity = layers.Sum(l => l.Remaining);
            var value = layers.Sum(l => decimal.Round(
                l.Remaining * l.Layer.UnitCost, 4, MidpointRounding.ToEven));

            result.Add(new StockOnHand(
                item.Id, item.Code, item.Name, item.BaseUom, quantity, value,
                // A layered design can always report an average; the reverse is not true,
                // which is why layers are the foundation.
                quantity > 0 ? decimal.Round(value / quantity, 4, MidpointRounding.ToEven) : null));
        }

        return result;
    }

    public async Task<IReadOnlyList<CostLayerDetail>> GetLayersAsync(
        Guid legalEntityId, Guid itemId, CancellationToken ct = default)
    {
        var layers = await LayersWithRemainingAsync(legalEntityId, itemId, ct);

        return layers
            .OrderBy(l => l.Layer.Sequence)
            .Select(l => new CostLayerDetail(
                l.Layer.Id,
                l.Layer.Sequence,
                l.Layer.ReceivedOn,
                l.Layer.QuantityReceived,
                l.Remaining,
                l.Layer.UnitCost,
                decimal.Round(l.Remaining * l.Layer.UnitCost, 4, MidpointRounding.ToEven),
                l.Layer.AdjustsLayerId))
            .ToList();
    }

    public async Task<IReadOnlyList<StockMoveSummary>> GetMovesAsync(
        Guid legalEntityId, Guid? itemId, CancellationToken ct = default)
        => await db.StockMoves
            .AsNoTracking()
            .Where(m => m.LegalEntityId == legalEntityId)
            .Where(m => itemId == null || m.ItemId == itemId)
            .OrderByDescending(m => m.MovedOn)
            .ThenByDescending(m => m.PostedAtUtc)
            .Select(m => new StockMoveSummary(
                m.Id, m.ItemId, m.Item!.Code, m.Item.Name, m.Direction.ToString(),
                m.Quantity, m.MovedOn, m.SourceDocumentType, m.JournalEntryId, m.Description))
            .ToListAsync(ct);

    // ---------------------------------------------------------------- helpers

    private sealed record LayerWithRemaining(CostLayer Layer, decimal Remaining);

    /// <summary>
    /// Layers with anything left, remaining computed rather than read.
    /// </summary>
    /// <remarks>
    /// Adjustment layers are excluded from consumption: they carry a revised unit cost for
    /// reporting but received no quantity of their own, so consuming from them would invent
    /// stock.
    /// </remarks>
    private async Task<List<LayerWithRemaining>> LayersWithRemainingAsync(
        Guid legalEntityId, Guid itemId, CancellationToken ct)
    {
        var layers = await db.CostLayers
            .AsNoTracking()
            .Where(l => l.LegalEntityId == legalEntityId && l.ItemId == itemId)
            .ToListAsync(ct);

        var consumedByLayer = await db.CostConsumptions
            .AsNoTracking()
            .Where(c => layers.Select(l => l.Id).Contains(c.CostLayerId))
            .GroupBy(c => c.CostLayerId)
            .Select(g => new { LayerId = g.Key, Quantity = g.Sum(c => c.Quantity) })
            .ToDictionaryAsync(x => x.LayerId, x => x.Quantity, ct);

        return layers
            .Select(l => new LayerWithRemaining(
                l, l.QuantityReceived - consumedByLayer.GetValueOrDefault(l.Id, 0m)))
            .Where(l => l.Remaining > 0)
            .ToList();
    }

    private async Task<long> NextSequenceAsync(Guid itemId, CancellationToken ct)
        => (await db.CostLayers
            .Where(l => l.ItemId == itemId)
            .MaxAsync(l => (long?)l.Sequence, ct) ?? 0L) + 1L;

    private Guid RequireUser()
        => currentUser.UserId
           ?? throw new PostingValidationException(
               "No acting user. A stock movement that cannot be attributed must not be posted.");

    private async Task<Item> RequireItemAsync(Guid itemId, CancellationToken ct)
        => await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct)
           ?? throw new NotFoundException($"No item with id {itemId}.");

    private async Task<Account> RequireAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new NotFoundException($"No account with id {accountId}.");

        if (!account.IsPostable)
        {
            throw new PostingValidationException(
                $"Account {account.Code} ({account.Name}) is a heading and cannot be posted to.");
        }

        return account;
    }

    private static ItemSummary Summarise(Item item) => new(
        item.Id, item.Code, item.Name, item.BaseUom,
        item.InventoryAccountId, item.CostOfSalesAccountId, item.IsActive);
}
