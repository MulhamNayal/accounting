namespace Accounting.Api.Models;

/// <summary>
/// An append-only record of every period state transition — who closed or reopened a
/// period, when, and why.
/// </summary>
/// <remarks>
/// This is the answer to the weakness found in the incumbent system, where reopening a
/// closed period meant editing a settings row and left no distinct trace. Rows here are
/// never updated or deleted; the application role's privileges are revoked accordingly.
/// </remarks>
public class PeriodEvent
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid PeriodId { get; set; }
    public AccountingPeriod? Period { get; set; }

    public PeriodState FromState { get; set; }

    public PeriodState ToState { get; set; }

    public DateTimeOffset AtUtc { get; set; }

    public Guid ByUserId { get; set; }
    public AppUser? ByUser { get; set; }

    /// <summary>
    /// Mandatory. A reopened period without a stated reason is precisely what an auditor
    /// asks about.
    /// </summary>
    public required string Reason { get; set; }
}
