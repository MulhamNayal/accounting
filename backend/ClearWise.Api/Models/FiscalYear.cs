namespace ClearWise.Api.Models;

/// <summary>
/// A financial year for one entity. Entities may run different year-ends, so this is per
/// entity rather than per tenant.
/// </summary>
public class FiscalYear
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    /// <summary>Display code, e.g. "FY2026".</summary>
    public required string Code { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Once <see cref="PeriodState.HardClosed"/> the year is filed and final. There is no
    /// transition out of that state anywhere in the model.
    /// </summary>
    public PeriodState State { get; set; } = PeriodState.Open;

    public ICollection<AccountingPeriod> Periods { get; set; } = [];
}
