using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Data;

/// <summary>
/// Creates a demonstration tenant with two entities and a starter chart of accounts, so
/// there is something to look at before real data exists. Development only.
/// </summary>
/// <remarks>
/// Runs as the ordinary application role, through row level security, exactly as real
/// writes will. Seeding via a privileged back door would prove nothing about whether the
/// policies actually permit the application to work.
/// </remarks>
public static class DevDataSeeder
{
    /// <summary>Fixed so the frontend can address the demo tenant without a login.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("0195c0de-0000-4000-8000-000000000001");

    public static readonly Guid DemoUserId = Guid.Parse("0195c0de-0000-4000-8000-000000000002");

    /// <summary>
    /// Creates the demonstration tenant, giving the demo account <paramref name="demoPassword"/>.
    /// </summary>
    /// <remarks>
    /// The password is a parameter rather than a constant because this repository is public.
    /// A committed default would be the real sign-in credential of every deployed instance
    /// that forgot to override it. The caller reads it from configuration and skips seeding
    /// altogether when it is absent.
    /// </remarks>
    public static async Task SeedAsync(
        IServiceProvider services, string demoPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(demoPassword);

        using var scope = services.CreateScope();

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(DemoTenantId);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        // Each block guards itself rather than the whole method, so a database seeded by an
        // earlier version still picks up what was added since.
        await SeedTenantAsync(db, cancellationToken);
        await SeedUserAsync(db, demoPassword, cancellationToken);
        await SeedCalendarAsync(db, cancellationToken);
        await SeedNumberSeriesAsync(db, cancellationToken);
        await SeedCustomersAsync(db, cancellationToken);
        await SeedSuppliersAsync(db, cancellationToken);
        await BackfillSystemRolesAsync(db, cancellationToken);
        await SeedTaxAsync(db, cancellationToken);
    }

    /// <summary>
    /// Two Malaysian regimes — the historical GST and the current SST — so the
    /// effective-dating actually has something to distinguish.
    /// </summary>
    /// <remarks>
    /// GST ran from April 2015 to August 2018 and was input-reclaimable; SST replaced it and
    /// is not. An invoice back-dated into 2017 must use GST codes and one dated now must not,
    /// which is the behaviour worth being able to demonstrate.
    /// </remarks>
    private static async Task SeedTaxAsync(AccountingDbContext db, CancellationToken cancellationToken)
    {
        if (await db.TaxRegimes.AnyAsync(cancellationToken))
        {
            return;
        }

        var outputTax = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "2020", cancellationToken);
        var inputTax = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "1240", cancellationToken);

        if (outputTax is null)
        {
            return;
        }

        var gst = new TaxRegime
        {
            Id = Guid.NewGuid(),
            TenantId = DemoTenantId,
            Code = "MY-GST",
            Name = "Malaysia Goods and Services Tax (historical)",
            CountryCode = "MY",
            InputReclaimable = true,
            EffectiveFrom = new DateOnly(2015, 4, 1),
            EffectiveTo = new DateOnly(2018, 8, 31),
        };

        var sst = new TaxRegime
        {
            Id = Guid.NewGuid(),
            TenantId = DemoTenantId,
            Code = "MY-SST",
            Name = "Malaysia Sales and Service Tax",
            CountryCode = "MY",
            InputReclaimable = false,
            EffectiveFrom = new DateOnly(2018, 9, 1),
        };

        db.TaxRegimes.AddRange(gst, sst);

        (TaxRegime Regime, string Code, string Name, TaxKind Kind, decimal Rate, bool Input)[] codes =
        [
            (gst, "SR", "Standard rated", TaxKind.ValueAdded, 6m, true),
            (gst, "ZRL", "Zero rated (local)", TaxKind.ZeroRated, 0m, true),
            (gst, "ES", "Exempt supply", TaxKind.Exempt, 0m, false),
            (sst, "SV", "Service tax", TaxKind.ServiceTax, 8m, false),
            (sst, "SL", "Sales tax", TaxKind.SalesTax, 10m, false),
            (sst, "NA", "Not taxable", TaxKind.OutOfScope, 0m, false),
        ];

        foreach (var (regime, code, name, kind, rate, reclaimable) in codes)
        {
            db.TaxCodes.Add(new TaxCode
            {
                Id = Guid.NewGuid(),
                TenantId = DemoTenantId,
                TaxRegimeId = regime.Id,
                Code = code,
                Name = name,
                Kind = kind,
                Rate = rate,
                // A zero-rated code needs no account: there is nothing to post.
                OutputAccountId = rate > 0 ? outputTax.Id : null,
                InputAccountId = reclaimable ? inputTax?.Id : null,
                EffectiveFrom = regime.EffectiveFrom,
                EffectiveTo = regime.EffectiveTo,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks the well-known accounts on a database seeded before <see cref="AccountSystemRole"/>
    /// existed.
    /// </summary>
    /// <remarks>
    /// Development only, and matched by code, which is exactly what the role column exists
    /// to avoid — but this is fixing up a demo chart the seeder created and whose codes it
    /// therefore knows. Nothing in the application resolves an account by code.
    /// </remarks>
    private static async Task BackfillSystemRolesAsync(
        AccountingDbContext db, CancellationToken cancellationToken)
    {
        (string Code, string Name, AccountType Type, AccountSystemRole Role)[] roles =
        [
            ("4900", "Realised Foreign Exchange Gain/Loss", AccountType.Income,
                AccountSystemRole.RealisedFxGainLoss),
            ("4910", "Unrealised Foreign Exchange Gain/Loss", AccountType.Income,
                AccountSystemRole.UnrealisedFxGainLoss),
            ("3020", "Retained Earnings", AccountType.Equity,
                AccountSystemRole.RetainedEarnings),
            ("3030", "Currency Translation Reserve", AccountType.Equity,
                AccountSystemRole.CurrencyTranslationReserve),
        ];

        var tenantId = DemoTenantId;
        var changed = false;

        foreach (var (code, name, type, role) in roles)
        {
            if (await db.Accounts.AnyAsync(a => a.SystemRole == role, cancellationToken))
            {
                continue;
            }

            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code, cancellationToken);

            if (account is null)
            {
                // A role added after the chart was first seeded has no account to mark, so
                // one is created. Only reachable in a development database that predates the
                // role; a real chart is the customer's to define.
                account = new Account
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = code,
                    Name = name,
                    AccountType = type,
                };
                db.Accounts.Add(account);
            }

            account.SystemRole = role;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedCustomersAsync(
        AccountingDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(cancellationToken))
        {
            return;
        }

        (string Code, string Name, string Currency, int Terms)[] customers =
        [
            ("C0001", "Anggun Properties Sdn Bhd", "MYR", 30),
            ("C0002", "Bayu Ventures Sdn Bhd", "MYR", 14),
            ("C0003", "Cendana Retail Sdn Bhd", "MYR", 60),
            ("C0004", "Overseas Holdings Pte Ltd", "SGD", 30),
        ];

        foreach (var (code, name, currency, terms) in customers)
        {
            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = DemoTenantId,
                Code = code,
                Name = name,
                CurrencyCode = currency,
                CreditTermDays = terms,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSuppliersAsync(
        AccountingDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Suppliers.AnyAsync(cancellationToken))
        {
            return;
        }

        (string Code, string Name, string Currency, int Terms)[] suppliers =
        [
            ("S0001", "Damai Office Supplies Sdn Bhd", "MYR", 30),
            ("S0002", "Enggang Facilities Management Sdn Bhd", "MYR", 14),
            ("S0003", "Firdaus Professional Services", "MYR", 30),
            // Foreign-currency, so settling one of its bills exercises the realised exchange
            // difference on the payables side.
            ("S0004", "Global Software Ltd", "USD", 45),
        ];

        foreach (var (code, name, currency, terms) in suppliers)
        {
            db.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = DemoTenantId,
                Code = code,
                Name = name,
                CurrencyCode = currency,
                CreditTermDays = terms,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A number series per entity for each document type that exists so far.
    /// </summary>
    /// <remarks>
    /// Journals are gappy: nobody audits a journal voucher sequence for density, and paying
    /// the serialisation cost there buys nothing. Sales invoices and credit notes are
    /// gapless, because a tax authority does examine those and a hole invites the question
    /// "where did that invoice go".
    /// </remarks>
    private static async Task SeedNumberSeriesAsync(
        AccountingDbContext db, CancellationToken cancellationToken)
    {
        var entities = await db.LegalEntities.ToListAsync(cancellationToken);

        (string DocumentType, string Code, string Name, string Format, bool Gapless)[] definitions =
        [
            // The year belongs in the format because the reset policy is yearly. Without it
            // the counter restarts each January and produces an entry number that already
            // exists, which the unique index on (entity, entry_no) refuses — so the first
            // posting of a new financial year fails. Every other series here already does this.
            ("JournalEntry", "JV", "Journal Voucher", "JV-{1:yyyy}-{0:D5}", false),
            ("SalesInvoice", "IV", "Sales Invoice", "IV-{1:yyyy}-{0:D5}", true),
            ("CreditNote", "CN", "Credit Note", "CN-{1:yyyy}-{0:D5}", true),
            ("CustomerReceipt", "OR", "Official Receipt", "OR-{1:yyyy}-{0:D5}", false),
            // Purchases are numbered for our own filing, not for anyone else's inspection --
            // the number a tax authority cares about on a bill is the supplier's, which is
            // recorded separately and is what the duplicate check keys on. So neither of
            // these is gapless.
            ("PurchaseInvoice", "PI", "Purchase Invoice", "PI-{1:yyyy}-{0:D5}", false),
            ("SupplierPayment", "PV", "Payment Voucher", "PV-{1:yyyy}-{0:D5}", false),
            // Gapless: a credit note reduces tax owed, so a tax authority examines the
            // sequence for exactly the same reason it examines sales invoices.
            ("PurchaseCreditNote", "SC", "Supplier Credit Note", "SC-{1:yyyy}-{0:D5}", true),
        ];

        foreach (var entity in entities)
        {
            foreach (var (documentType, code, name, format, gapless) in definitions)
            {
                var exists = await db.NumberSeries.AnyAsync(
                    s => s.LegalEntityId == entity.Id && s.Code == code, cancellationToken);

                if (exists)
                {
                    continue;
                }

                db.NumberSeries.Add(new NumberSeries
                {
                    Id = Guid.NewGuid(),
                    TenantId = DemoTenantId,
                    LegalEntityId = entity.Id,
                    DocumentType = documentType,
                    Code = code,
                    Name = name,
                    Format = format,
                    ResetPolicy = NumberResetPolicy.Yearly,
                    IsGapless = gapless,
                    IsDefault = true,
                    IsActive = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The user every posted entry is attributed to until authentication exists. Guarded
    /// separately from the tenant, because a database seeded before this existed still
    /// needs it — every journal entry has a foreign key to it.
    /// </summary>
    private static async Task SeedUserAsync(
        AccountingDbContext db, string demoPassword, CancellationToken cancellationToken)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == DemoUserId, cancellationToken);

        if (existing is not null)
        {
            // The demo account's password is whatever is currently configured, reasserted on
            // every start rather than set once. Setting it only when absent meant that
            // correcting a wrong Seed:DemoPassword had no effect on an existing database, so
            // the credential in configuration and the one that actually worked drifted apart
            // with nothing to indicate it. This is safe precisely because it is a demo
            // account; no real user's password is managed here.
            existing.PasswordHash = AuthService.HashPassword(existing, demoPassword);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var user = new AppUser
        {
            Id = DemoUserId,
            TenantId = DemoTenantId,
            Email = "demo@accounting.test",
            DisplayName = "Demo User",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        // Hashed with the same algorithm as a real sign-in, so the seeded account exercises
        // the actual code path rather than a shortcut around it.
        user.PasswordHash = AuthService.HashPassword(user, demoPassword);

        db.Users.Add(user);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTenantAsync(AccountingDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        db.Tenants.Add(new Tenant
        {
            Id = DemoTenantId,
            Name = "Demo Group",
            CreatedAtUtc = now,
        });

        var holdings = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = DemoTenantId,
            Code = "HOLD",
            Name = "Demo Holdings Sdn Bhd",
            FunctionalCurrency = "MYR",
            FinancialYearStartMonth = 1,
            CreatedAtUtc = now,
        };

        var realty = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = DemoTenantId,
            Code = "RLTY",
            Name = "Demo Realty Sdn Bhd",
            FunctionalCurrency = "MYR",
            FinancialYearStartMonth = 1,
            CreatedAtUtc = now,
        };

        db.LegalEntities.AddRange(holdings, realty);

        var accounts = BuildStarterChart(DemoTenantId);
        db.Accounts.AddRange(accounts);

        // Both entities activate every account in the starter chart. Divergence is the
        // point of EntityAccount, but a demo has nothing to diverge on yet.
        foreach (var entity in new[] { holdings, realty })
        {
            foreach (var account in accounts.Where(a => a.IsPostable))
            {
                db.EntityAccounts.Add(new EntityAccount
                {
                    Id = Guid.NewGuid(),
                    TenantId = DemoTenantId,
                    LegalEntityId = entity.Id,
                    AccountId = account.Id,
                    IsActive = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A fiscal year and twelve monthly periods per entity, so entries can actually be
    /// posted. Nothing posts without an open period covering its date.
    /// </summary>
    private static async Task SeedCalendarAsync(AccountingDbContext db, CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var entities = await db.LegalEntities.ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            if (await db.FiscalYears.AnyAsync(
                    f => f.LegalEntityId == entity.Id && f.Code == $"FY{year}", cancellationToken))
            {
                continue;
            }

            var fiscalYearId = Guid.NewGuid();

            db.FiscalYears.Add(new FiscalYear
            {
                Id = fiscalYearId,
                TenantId = DemoTenantId,
                LegalEntityId = entity.Id,
                Code = $"FY{year}",
                StartDate = new DateOnly(year, 1, 1),
                EndDate = new DateOnly(year, 12, 31),
                State = PeriodState.Open,
            });

            for (var month = 1; month <= 12; month++)
            {
                db.Periods.Add(new AccountingPeriod
                {
                    Id = Guid.NewGuid(),
                    TenantId = DemoTenantId,
                    LegalEntityId = entity.Id,
                    FiscalYearId = fiscalYearId,
                    Sequence = month,
                    StartDate = new DateOnly(year, month, 1),
                    EndDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
                    State = PeriodState.Open,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A deliberately small Malaysian SME chart. Control accounts are marked, because a
    /// posting to one must carry its dimension — that constraint arrives in Layer 1.
    /// </summary>
    private static List<Account> BuildStarterChart(Guid tenantId)
    {
        var accounts = new List<Account>();

        Account Add(string code, string name, AccountType type, Account? parent = null,
                    bool postable = true, ControlType control = ControlType.None,
                    AccountSystemRole role = AccountSystemRole.None)
        {
            var account = new Account
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                AccountType = type,
                ParentId = parent?.Id,
                IsPostable = postable,
                ControlType = control,
                SystemRole = role,
            };
            accounts.Add(account);
            return account;
        }

        var nonCurrentAssets = Add("1000", "Non-Current Assets", AccountType.Asset, postable: false);
        Add("1100", "Property, Plant and Equipment", AccountType.Asset, nonCurrentAssets);

        var currentAssets = Add("1200", "Current Assets", AccountType.Asset, postable: false);
        Add("1210", "Trade Receivables", AccountType.Asset, currentAssets, control: ControlType.AccountsReceivable);
        Add("1220", "Inventory", AccountType.Asset, currentAssets, control: ControlType.Stock);
        Add("1230", "Cash and Bank", AccountType.Asset, currentAssets, control: ControlType.Bank);
        Add("1240", "Input Tax", AccountType.Asset, currentAssets, control: ControlType.Tax);

        var liabilities = Add("2000", "Current Liabilities", AccountType.Liability, postable: false);
        Add("2010", "Trade Payables", AccountType.Liability, liabilities, control: ControlType.AccountsPayable);
        Add("2020", "Output Tax", AccountType.Liability, liabilities, control: ControlType.Tax);
        Add("2030", "Accruals", AccountType.Liability, liabilities);

        var equity = Add("3000", "Equity", AccountType.Equity, postable: false);
        Add("3010", "Share Capital", AccountType.Equity, equity);
        Add("3020", "Retained Earnings", AccountType.Equity, equity,
            role: AccountSystemRole.RetainedEarnings);
        // Consolidating entities with different functional currencies cannot balance, and the
        // residue belongs in equity rather than profit. Without this account the run refuses.
        Add("3030", "Currency Translation Reserve", AccountType.Equity, equity,
            role: AccountSystemRole.CurrencyTranslationReserve);

        var revenue = Add("4000", "Revenue", AccountType.Income, postable: false);
        Add("4010", "Sales", AccountType.Income, revenue);
        Add("4020", "Commission Income", AccountType.Income, revenue);
        Add("4900", "Realised Foreign Exchange Gain/Loss", AccountType.Income, revenue,
            role: AccountSystemRole.RealisedFxGainLoss);
        Add("4910", "Unrealised Foreign Exchange Gain/Loss", AccountType.Income, revenue,
            role: AccountSystemRole.UnrealisedFxGainLoss);

        var costOfSales = Add("5000", "Cost of Sales", AccountType.Expense, postable: false);
        Add("5010", "Cost of Goods Sold", AccountType.Expense, costOfSales);

        var opex = Add("6000", "Operating Expenses", AccountType.Expense, postable: false);
        Add("6010", "Salaries and Wages", AccountType.Expense, opex);
        Add("6020", "Rent", AccountType.Expense, opex);
        Add("6030", "Utilities", AccountType.Expense, opex);

        return accounts;
    }
}
