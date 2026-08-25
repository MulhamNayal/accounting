using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Data;

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

    /// <summary>Stands in for an authenticated principal until authentication exists.</summary>
    public static readonly Guid DemoUserId = Guid.Parse("0195c0de-0000-4000-8000-000000000002");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(DemoTenantId);

        var db = scope.ServiceProvider.GetRequiredService<ClearWiseDbContext>();

        // Each block guards itself rather than the whole method, so a database seeded by an
        // earlier version still picks up what was added since.
        await SeedTenantAsync(db, cancellationToken);
        await SeedUserAsync(db, cancellationToken);
        await SeedCalendarAsync(db, cancellationToken);
    }

    /// <summary>
    /// The user every posted entry is attributed to until authentication exists. Guarded
    /// separately from the tenant, because a database seeded before this existed still
    /// needs it — every journal entry has a foreign key to it.
    /// </summary>
    private static async Task SeedUserAsync(ClearWiseDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Id == DemoUserId, cancellationToken))
        {
            return;
        }

        db.Users.Add(new AppUser
        {
            Id = DemoUserId,
            TenantId = DemoTenantId,
            Email = "demo@clearwise.test",
            DisplayName = "Demo User",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTenantAsync(ClearWiseDbContext db, CancellationToken cancellationToken)
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
    private static async Task SeedCalendarAsync(ClearWiseDbContext db, CancellationToken cancellationToken)
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
                    bool postable = true, ControlType control = ControlType.None)
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
        Add("3020", "Retained Earnings", AccountType.Equity, equity);

        var revenue = Add("4000", "Revenue", AccountType.Income, postable: false);
        Add("4010", "Sales", AccountType.Income, revenue);
        Add("4020", "Commission Income", AccountType.Income, revenue);
        Add("4900", "Realised Foreign Exchange Gain/Loss", AccountType.Income, revenue);
        Add("4910", "Unrealised Foreign Exchange Gain/Loss", AccountType.Income, revenue);

        var costOfSales = Add("5000", "Cost of Sales", AccountType.Expense, postable: false);
        Add("5010", "Cost of Goods Sold", AccountType.Expense, costOfSales);

        var opex = Add("6000", "Operating Expenses", AccountType.Expense, postable: false);
        Add("6010", "Salaries and Wages", AccountType.Expense, opex);
        Add("6020", "Rent", AccountType.Expense, opex);
        Add("6030", "Utilities", AccountType.Expense, opex);

        return accounts;
    }
}
