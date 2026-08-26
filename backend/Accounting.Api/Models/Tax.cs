namespace Accounting.Api.Models;

/// <summary>
/// What kind of tax a code represents.
/// </summary>
/// <remarks>
/// The distinction that matters is not the local name but whether tax paid on purchases can
/// be reclaimed. A VAT/GST system nets input against output; a sales tax like Malaysia's SST
/// does not, and treating them the same produces a wrong return in one jurisdiction or the
/// other.
/// </remarks>
public enum TaxKind
{
    /// <summary>Sales tax with no input reclaim — Malaysia SST, most US state taxes.</summary>
    SalesTax = 1,

    /// <summary>Service tax with no input reclaim.</summary>
    ServiceTax = 2,

    /// <summary>VAT or GST: input tax is reclaimable against output tax.</summary>
    ValueAdded = 3,

    /// <summary>Deducted at source and remitted on the payee's behalf.</summary>
    Withholding = 4,

    /// <summary>In scope but exempt — no tax, and input tax may be irrecoverable.</summary>
    Exempt = 5,

    /// <summary>In scope at 0%. Distinct from exempt: input tax stays recoverable.</summary>
    ZeroRated = 6,

    /// <summary>Outside the scope of the regime entirely.</summary>
    OutOfScope = 7,
}

/// <summary>
/// One jurisdiction's tax system, as it applies to an entity.
/// </summary>
/// <remarks>
/// The unit of internationalisation. Adding a country means adding a regime and its codes,
/// not changing any posting logic — the rule reads <see cref="TaxCode.Rate"/> and
/// <see cref="InputReclaimable"/> and does not know or care which country it is in.
/// </remarks>
public class TaxRegime
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Stable identifier, e.g. "MY-SST", "SG-GST", "GB-VAT".</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public required string CountryCode { get; set; }

    /// <summary>
    /// Whether tax on purchases can be offset against tax on sales. True for VAT/GST,
    /// false for a sales tax. This single flag is most of what separates the two families.
    /// </summary>
    public bool InputReclaimable { get; set; }

    /// <summary>
    /// When this regime came into force. Malaysia had GST from 2015 to 2018 and SST since,
    /// so a historical document belongs to whichever regime was in force on its date.
    /// </summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null while current. Set when a regime is replaced.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<TaxCode> Codes { get; set; } = [];
}

/// <summary>
/// A rate within a regime, as applied to a document line.
/// </summary>
/// <remarks>
/// Postings store the <c>TaxCodeId</c>, never the rate. Because postings are immutable, a
/// document keeps the code it was posted under for good — which is how a superseded regime's
/// history survives without being restated. A code is retired by setting
/// <see cref="EffectiveTo"/>, never by editing its rate.
/// </remarks>
public class TaxCode
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid TaxRegimeId { get; set; }
    public TaxRegime? TaxRegime { get; set; }

    /// <summary>Short code as it appears on a return, e.g. "SR", "ZRL", "ES", "S-6".</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public TaxKind Kind { get; set; }

    /// <summary>Percentage, so 6% is 6, not 0.06.</summary>
    public decimal Rate { get; set; }

    /// <summary>Where tax charged on a sale is credited. Required unless the rate is zero.</summary>
    public Guid? OutputAccountId { get; set; }
    public Account? OutputAccount { get; set; }

    /// <summary>
    /// Where tax paid on a purchase is debited. Null when the regime does not allow reclaim,
    /// in which case input tax is a cost rather than an asset.
    /// </summary>
    public Guid? InputAccountId { get; set; }
    public Account? InputAccount { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>True on the given date, whatever the current date is.</summary>
    public bool AppliesOn(DateOnly date) =>
        IsActive && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}
