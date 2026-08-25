namespace ClearWise.Api.Models;

/// <summary>Which way stock moved.</summary>
public enum StockDirection
{
    /// <summary>Into stock — a purchase, a return from a customer, a positive adjustment.</summary>
    In = 1,

    /// <summary>Out of stock — a sale, a write-off, a negative adjustment.</summary>
    Out = 2,
}

/// <summary>
/// Something the business holds in stock.
/// </summary>
/// <remarks>
/// Carries no quantity and no value. Both are derived: quantity from the moves, value from
/// the unconsumed cost layers. A stored on-hand figure is the classic source of stock that
/// disagrees with the inventory account.
/// </remarks>
public class Item
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The unit quantities are expressed in. Multi-UOM conversion is not in scope.</summary>
    public required string BaseUom { get; set; }

    /// <summary>Where the value of stock on hand sits — a stock control account.</summary>
    public Guid InventoryAccountId { get; set; }
    public Account? InventoryAccount { get; set; }

    /// <summary>Where the cost of stock sold is charged.</summary>
    public Guid CostOfSalesAccountId { get; set; }
    public Account? CostOfSalesAccount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// One movement of stock, in or out. Append-only, like everything that touches the ledger.
/// </summary>
public class StockMove
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    public StockDirection Direction { get; set; }

    /// <summary>Always positive. The direction carries the sign, never the quantity.</summary>
    public decimal Quantity { get; set; }

    public DateOnly MovedOn { get; set; }

    public required string SourceDocumentType { get; set; }

    public Guid? SourceDocumentId { get; set; }

    /// <summary>The entry this movement posted. Stock and its value move together.</summary>
    public Guid? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }

    public DateTimeOffset PostedAtUtc { get; set; }

    public Guid PostedByUserId { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// The cost basis of one receipt into stock.
/// </summary>
/// <remarks>
/// <b>There is no <c>QuantityRemaining</c> column.</b> Remaining is
/// <see cref="QuantityReceived"/> less the consumptions recorded against this layer, because
/// a stored remainder is a mutable field on an append-only table — and a wrong one silently
/// corrupts the cost basis of everything issued afterwards.
/// <para>
/// FIFO order is <see cref="Sequence"/>, not <see cref="ReceivedOn"/>: two receipts on the
/// same day still have a definite order, and a back-dated receipt must not reorder layers
/// that have already been consumed.
/// </para>
/// </remarks>
public class CostLayer
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>The receipt that created this layer.</summary>
    public Guid SourceMoveId { get; set; }
    public StockMove? SourceMove { get; set; }

    public decimal QuantityReceived { get; set; }

    /// <summary>Cost per unit in the entity's functional currency.</summary>
    public decimal UnitCost { get; set; }

    public DateOnly ReceivedOn { get; set; }

    /// <summary>Monotonic per item. Defines consumption order.</summary>
    public long Sequence { get; set; }

    /// <summary>
    /// Set when this layer revises another's cost rather than being a fresh receipt.
    /// </summary>
    /// <remarks>
    /// A retroactive cost change never edits the original layer — its consumptions already
    /// posted at the cost that was true when they happened. Instead a new layer records the
    /// revised basis for whatever is still on hand.
    /// </remarks>
    public Guid? AdjustsLayerId { get; set; }
    public CostLayer? AdjustsLayer { get; set; }

    public ICollection<CostConsumption> Consumptions { get; set; } = [];
}

/// <summary>
/// Which layer an issue took its cost from, and how much.
/// </summary>
/// <remarks>
/// This is what makes cost of sales explainable rather than merely calculated: for any sale,
/// the exact receipts its cost came from can be named.
/// </remarks>
public class CostConsumption
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid CostLayerId { get; set; }
    public CostLayer? CostLayer { get; set; }

    /// <summary>The outward move that consumed it.</summary>
    public Guid OutMoveId { get; set; }
    public StockMove? OutMove { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Copied from the layer, so the arithmetic stays reproducible.</summary>
    public decimal UnitCost { get; set; }

    public decimal Amount { get; set; }
}
