using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;
using ClearWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearWise.Tests;

[Collection(nameof(DatabaseCollection))]
public class StockTests
{
    private static readonly DateOnly August = new(2026, 8, 15);

    private sealed record Kit(
        IStockService Stock,
        IPostingService Postings,
        ClearWiseDbContext Db) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static Kit KitFor(LedgerWorld world)
    {
        var db = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(db);
        var postings = new PostingService(db, user, numbers, NullLogger<PostingService>.Instance);
        return new Kit(
            new StockService(db, user, postings, NullLogger<StockService>.Instance), postings, db);
    }

    /// <summary>Inventory, cost of sales and a payables account, plus an item using them.</summary>
    private static async Task<(Guid ItemId, Guid InventoryId, Guid CogsId, Guid PayablesId)>
        SeedItemAsync(LedgerWorld world)
    {
        Guid inventoryId, cogsId, payablesId;

        await using (var db = world.NewAppContext())
        {
            var inventory = new Account
            {
                Id = Guid.NewGuid(), TenantId = world.TenantId, Code = "1220", Name = "Inventory",
                AccountType = AccountType.Asset, ControlType = ControlType.Stock,
            };
            var cogs = new Account
            {
                Id = Guid.NewGuid(), TenantId = world.TenantId, Code = "5010",
                Name = "Cost of Goods Sold", AccountType = AccountType.Expense,
            };
            var payables = new Account
            {
                Id = Guid.NewGuid(), TenantId = world.TenantId, Code = "2010",
                Name = "Trade Payables", AccountType = AccountType.Liability,
            };

            db.Accounts.AddRange(inventory, cogs, payables);
            await db.SaveChangesAsync();

            inventoryId = inventory.Id;
            cogsId = cogs.Id;
            payablesId = payables.Id;
        }

        await using var kitDb = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(kitDb);
        var stock = new StockService(
            kitDb, user,
            new PostingService(kitDb, user, numbers, NullLogger<PostingService>.Instance),
            NullLogger<StockService>.Instance);

        var item = await stock.CreateItemAsync(new CreateItemRequest(
            "WIDGET", "Standard widget", "unit", inventoryId, cogsId));

        return (item.Id, inventoryId, cogsId, payablesId);
    }

    // ---------------------------------------------------------------- receipts

    [Fact]
    public async Task Receipt_CreatesALayerAndDebitsInventory()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, inventoryId, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 10m, August, payablesId));

        var layers = await kit.Stock.GetLayersAsync(world.EntityId, itemId);
        Assert.Single(layers);
        Assert.Equal(10m, layers[0].QuantityReceived);
        Assert.Equal(10m, layers[0].QuantityRemaining);
        Assert.Equal(10m, layers[0].UnitCost);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        Assert.Equal(100m, trialBalance.Lines.Single(l => l.AccountId == inventoryId).Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task Receipt_WithoutACost_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Stock.ReceiveAsync(new ReceiveStockRequest(
                world.EntityId, itemId, 10m, 0m, August, payablesId)));

        Assert.Contains("positive unit cost", ex.Message);
    }

    [Fact]
    public async Task Item_MustUseAStockControlAccountForInventory()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var kit = KitFor(world);

        // The sales account is not a stock control account, so stock value could never be
        // reconciled against the subledger.
        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Stock.CreateItemAsync(new CreateItemRequest(
                "BAD", "Wrong accounts", "unit", world.SalesAccountId, world.SalesAccountId)));

        Assert.Contains("not a stock control account", ex.Message);
    }

    // ---------------------------------------------------------------- FIFO

    /// <summary>
    /// The worked example from the design discussion.
    /// </summary>
    [Fact]
    public async Task Issue_ConsumesOldestFirst_AndCostOfSalesIsWhatWasActuallyConsumed()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, inventoryId, cogsId, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        // Buy 10 at 10, then 10 at 14. Sell 12.
        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 10m, August, payablesId));
        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));

        var issue = await kit.Stock.IssueAsync(new IssueStockRequest(
            world.EntityId, itemId, 12m, August));

        // FIFO: all 10 at 10, then 2 at 14. 100 + 28 = 128.
        // A weighted average would have given 12 x 12 = 144.
        Assert.Equal(128m, issue.TotalCost);

        // And it can name which receipts the cost came from.
        Assert.Equal(2, issue.Consumed.Count);
        Assert.Equal(10m, issue.Consumed[0].Quantity);
        Assert.Equal(10m, issue.Consumed[0].UnitCost);
        Assert.Equal(2m, issue.Consumed[1].Quantity);
        Assert.Equal(14m, issue.Consumed[1].UnitCost);

        var onHand = (await kit.Stock.GetOnHandAsync(world.EntityId)).Single();
        Assert.Equal(8m, onHand.QuantityOnHand);
        Assert.Equal(112m, onHand.ValueOnHand);      // 8 remaining at 14
        Assert.Equal(14m, onHand.AverageUnitCost);   // layers can always report an average

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        Assert.Equal(112m, trialBalance.Lines.Single(l => l.AccountId == inventoryId).Balance);
        Assert.Equal(128m, trialBalance.Lines.Single(l => l.AccountId == cogsId).Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task Issue_ExhaustingALayer_LeavesItWithNothingRemaining()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 5m, 20m, August, payablesId));
        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 5m, 30m, August, payablesId));

        await kit.Stock.IssueAsync(new IssueStockRequest(world.EntityId, itemId, 5m, August));

        // The exhausted layer drops out entirely; remaining is derived, not stored.
        var layers = await kit.Stock.GetLayersAsync(world.EntityId, itemId);
        Assert.Single(layers);
        Assert.Equal(30m, layers[0].UnitCost);
    }

    [Fact]
    public async Task Issuing_MoreThanIsOnHand_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 3m, 10m, August, payablesId));

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Stock.IssueAsync(new IssueStockRequest(world.EntityId, itemId, 5m, August)));

        Assert.Contains("on hand", ex.Message);
    }

    [Fact]
    public async Task StockRecords_AreAppendOnly()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 10m, August, payablesId));

        await using var editor = world.NewAppContext();
        var layer = await editor.CostLayers.FirstAsync();
        layer.UnitCost = 1m;

        // Rewriting a cost basis would leave stock valued at something the inventory
        // account never recorded.
        var ex = await Record.ExceptionAsync(() => editor.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.Contains("permission denied", ex!.GetBaseException().Message);
    }

    // ---------------------------------------------------------------- the cascade

    /// <summary>
    /// The hardest thing in the system: a cost discovered wrong after some of the stock has
    /// already been sold.
    /// </summary>
    [Fact]
    public async Task CostCorrection_SplitsBetweenStockStillHeldAndStockAlreadySold()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, inventoryId, cogsId, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        // Received 10 at 14, sold 6, then learn the true cost was 15.
        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        await kit.Stock.IssueAsync(new IssueStockRequest(world.EntityId, itemId, 6m, August));

        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        var result = await kit.Stock.AdjustCostAsync(new AdjustCostRequest(
            world.EntityId, layer.Id, 15m, August, payablesId, "Supplier debit note"));

        Assert.Equal(1m, result.Difference);
        Assert.Equal(4m, result.QuantityStillOnHand);
        Assert.Equal(6m, result.QuantityAlreadySold);

        // 4 still held are worth 1 more each; 6 already sold were costed 1 too little.
        Assert.Equal(4m, result.InventoryAdjustment);
        Assert.Equal(6m, result.CostOfSalesAdjustment);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));

        // Inventory: 4 remaining at the corrected 15.
        Assert.Equal(60m, trialBalance.Lines.Single(l => l.AccountId == inventoryId).Balance);
        // Cost of sales: 6 at the corrected 15.
        Assert.Equal(90m, trialBalance.Lines.Single(l => l.AccountId == cogsId).Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task CostCorrection_LeavesTheOriginalLayerUntouched()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        await kit.Stock.AdjustCostAsync(new AdjustCostRequest(
            world.EntityId, layer.Id, 15m, August, payablesId));

        await using var reader = world.NewAppContext();
        var original = await reader.CostLayers.AsNoTracking().FirstAsync(l => l.Id == layer.Id);

        // Its consumptions posted at the cost that was true when they happened.
        Assert.Equal(14m, original.UnitCost);

        var adjustment = await reader.CostLayers.AsNoTracking()
            .FirstAsync(l => l.AdjustsLayerId == layer.Id);
        Assert.Equal(15m, adjustment.UnitCost);
        // Brings no quantity of its own — it revises cost, it does not receive stock.
        Assert.Equal(0m, adjustment.QuantityReceived);
    }

    [Fact]
    public async Task CostCorrection_WhenNothingHasBeenSold_TouchesOnlyInventory()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, cogsId, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        var result = await kit.Stock.AdjustCostAsync(new AdjustCostRequest(
            world.EntityId, layer.Id, 15m, August, payablesId));

        Assert.Equal(10m, result.InventoryAdjustment);
        Assert.Equal(0m, result.CostOfSalesAdjustment);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));
        Assert.DoesNotContain(trialBalance.Lines, l => l.AccountId == cogsId);
    }

    [Fact]
    public async Task CostCorrection_Downwards_ReversesTheSides()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, inventoryId, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        var result = await kit.Stock.AdjustCostAsync(new AdjustCostRequest(
            world.EntityId, layer.Id, 12m, August, payablesId));

        Assert.Equal(-2m, result.Difference);

        var trialBalance = await kit.Postings.GetTrialBalanceAsync(
            world.EntityId, new DateOnly(2026, 12, 31));

        // 10 units now at 12 rather than 14.
        Assert.Equal(120m, trialBalance.Lines.Single(l => l.AccountId == inventoryId).Balance);
        Assert.True(trialBalance.IsBalanced);
    }

    [Fact]
    public async Task CostCorrection_ToTheSameCost_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => kit.Stock.AdjustCostAsync(new AdjustCostRequest(
                world.EntityId, layer.Id, 14m, August, payablesId)));

        Assert.Contains("Nothing to correct", ex.Message);
    }

    [Fact]
    public async Task FutureIssues_ConsumeAtTheCorrectedCost()
    {
        var world = await LedgerFixture.CreateAsync();
        var (itemId, _, _, payablesId) = await SeedItemAsync(world);
        await using var kit = KitFor(world);

        await kit.Stock.ReceiveAsync(new ReceiveStockRequest(
            world.EntityId, itemId, 10m, 14m, August, payablesId));
        var layer = (await kit.Stock.GetLayersAsync(world.EntityId, itemId)).Single();

        await kit.Stock.AdjustCostAsync(new AdjustCostRequest(
            world.EntityId, layer.Id, 15m, August, payablesId));

        // The original layer still holds the quantity, so this consumes at 14 and the
        // correction has already accounted for the difference on what remains.
        var issue = await kit.Stock.IssueAsync(new IssueStockRequest(
            world.EntityId, itemId, 2m, August));

        Assert.Equal(28m, issue.TotalCost);
    }
}
