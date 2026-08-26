namespace Accounting.Api.Models;

/// <summary>
/// One posting period, normally a calendar month within a <see cref="FiscalYear"/>.
/// The period an entry belongs to is resolved from its accounting date, not from the wall
/// clock — back-dating into an open period is ordinary and permitted.
/// </summary>
public class AccountingPeriod
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid LegalEntityId { get; set; }
    public LegalEntity? LegalEntity { get; set; }

    public Guid FiscalYearId { get; set; }
    public FiscalYear? FiscalYear { get; set; }

    /// <summary>Ordinal within the fiscal year, starting at 1.</summary>
    public int Sequence { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public PeriodState State { get; set; } = PeriodState.Open;

    public ICollection<PeriodEvent> Events { get; set; } = [];

    public bool AcceptsPostings => State == PeriodState.Open;
}
