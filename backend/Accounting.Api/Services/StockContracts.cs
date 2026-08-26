namespace Accounting.Api.Services;

public record CreateItemRequest(
    string Code,
    string Name,
    string BaseUom,
    Guid InventoryAccountId,
    Guid CostOfSalesAccountId,
    string? Description = null);

public record ItemSummary(
    Guid Id,
    string Code,
    string Name,
    string BaseUom,
    Guid InventoryAccountId,
    Guid CostOfSalesAccountId,
    bool IsActive);

public record ReceiveStockRequest(
    Guid LegalEntityId,
    Guid ItemId,
    decimal Quantity,
    decimal UnitCost,
    DateOnly MovedOn,
    /// <summary>What is credited â€” trade payables, an accrual, or an opening-balance account.</summary>
    Guid CreditAccountId,
    Guid? SupplierId = null,
    string? Description = null);

public record IssueStockRequest(
    Guid LegalEntityId,
    Guid ItemId,
    decimal Quantity,
    DateOnly MovedOn,
    Guid? CustomerId = null,
    string? Description = null);

/// <summary>
/// A cost correction to a receipt already made.
/// </summary>
/// <remarks>
/// Never an edit. The original layer's consumptions posted at the cost that was true when
/// they happened, and restating them would change figures already reported.
/// </remarks>
public record AdjustCostRequest(
    Guid LegalEntityId,
    Guid CostLayerId,
    decimal CorrectedUnitCost,
    DateOnly AdjustedOn,
    /// <summary>What the difference is charged to or credited from â€” usually trade payables.</summary>
    Guid CounterAccountId,
    string? Reason = null);

public record StockMoveSummary(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string Direction,
    decimal Quantity,
    DateOnly MovedOn,
    string SourceDocumentType,
    Guid? JournalEntryId,
    string? Description);

/// <summary>
/// Quantity and value on hand, both derived â€” quantity from the moves, value from the
/// unconsumed layers.
/// </summary>
public record StockOnHand(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string BaseUom,
    decimal QuantityOnHand,
    decimal ValueOnHand,
    /// <summary>Value divided by quantity. A FIFO design can always report an average.</summary>
    decimal? AverageUnitCost);

public record CostLayerDetail(
    Guid Id,
    long Sequence,
    DateOnly ReceivedOn,
    decimal QuantityReceived,
    decimal QuantityRemaining,
    decimal UnitCost,
    decimal ValueRemaining,
    Guid? AdjustsLayerId);

/// <summary>
/// The result of an issue: what it cost, and which layers it came from.
/// </summary>
public record StockIssueResult(
    Guid MoveId,
    Guid? JournalEntryId,
    decimal Quantity,
    decimal TotalCost,
    IReadOnlyList<ConsumptionDetail> Consumed);

public record ConsumptionDetail(
    Guid CostLayerId,
    long LayerSequence,
    decimal Quantity,
    decimal UnitCost,
    decimal Amount);

/// <summary>
/// How a retroactive cost change was split.
/// </summary>
/// <remarks>
/// The quantity still on hand is worth more or less than recorded, so that share adjusts
/// inventory. The quantity already sold was costed wrong, so that share adjusts cost of
/// sales â€” in the current open period, never by restating the original.
/// </remarks>
public record CostAdjustmentResult(
    Guid NewLayerId,
    Guid JournalEntryId,
    decimal Difference,
    decimal QuantityStillOnHand,
    decimal QuantityAlreadySold,
    decimal InventoryAdjustment,
    decimal CostOfSalesAdjustment);
