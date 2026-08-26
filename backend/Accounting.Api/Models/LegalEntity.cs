namespace Accounting.Api.Models;

/// <summary>
/// One legal company within a tenant — "Holdings" and "Realty" are two of these, not two
/// tenants. Each keeps its own books, financial year and tax identity, but they share a
/// chart of accounts and customer master so they can be consolidated.
/// </summary>
/// <remarks>
/// Named <c>LegalEntity</c> rather than <c>Entity</c> to avoid collision with the ordinary
/// EF Core sense of the word.
/// </remarks>
public class LegalEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Company registration number (SSM number in Malaysia).</summary>
    public string? RegistrationNo { get; set; }

    /// <summary>
    /// Tax identification number. Per entity, not per tenant: each company files
    /// separately, and e-Invoice is issued against a specific TIN.
    /// </summary>
    public string? TaxId { get; set; }

    /// <summary>ISO 4217 code. The currency this entity's books are kept in.</summary>
    public required string FunctionalCurrency { get; set; }

    /// <summary>Month (1-12) the financial year starts. Entities may differ.</summary>
    public int FinancialYearStartMonth { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<EntityAccount> EntityAccounts { get; set; } = [];
    public ICollection<FiscalYear> FiscalYears { get; set; } = [];
}
