namespace ClearWise.Api.Models;

/// <summary>
/// Someone who owes the business money.
/// </summary>
/// <remarks>
/// Held at tenant level, not per entity, deliberately. A group that bills the same client
/// from two companies needs one record for that client, or consolidation can never match
/// the two sides up and every address change has to be made twice.
/// <para>
/// There is no balance field here. What a customer owes is the sum of postings to a
/// receivables control account carrying their id — the same rows the control account itself
/// is computed from, so the two cannot disagree.
/// </para>
/// </remarks>
public class Customer
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Company registration number.</summary>
    public string? RegistrationNo { get; set; }

    /// <summary>Tax identification number — required on an e-Invoice from Layer 4.</summary>
    public string? TaxId { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? BillingAddress { get; set; }

    /// <summary>The currency this customer is normally billed in.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>Days from invoice date to due date.</summary>
    public int CreditTermDays { get; set; } = 30;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
