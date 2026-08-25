using ClearWise.Api.Data;
using ClearWise.Api.Models;

namespace ClearWise.Tests;

/// <summary>
/// A self-contained tenant with one entity, a few accounts, an open period and a closed
/// one. Each call creates a fresh tenant, so tests never see each other's rows.
/// </summary>
public sealed record LedgerWorld(
    Guid TenantId,
    Guid EntityId,
    Guid UserId,
    Guid OpenPeriodId,
    Guid ClosedPeriodId,
    Guid CashAccountId,
    Guid SalesAccountId,
    Guid ReceivablesAccountId,
    Guid HeadingAccountId)
{
    public ITenantContext Context()
    {
        var context = new TenantContext();
        context.SetTenant(TenantId);
        return context;
    }

    public ClearWiseDbContext NewAppContext() => TestDatabase.CreateAppContext(Context());
}

public static class LedgerFixture
{
    public static async Task<LedgerWorld> CreateAsync()
    {
        var tenantId = Guid.NewGuid();

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        await using var db = TestDatabase.CreateAppContext(tenantContext);

        var now = DateTimeOffset.UtcNow;

        db.Tenants.Add(new Tenant { Id = tenantId, Name = $"T{tenantId:N}", CreatedAtUtc = now });

        var entityId = Guid.NewGuid();
        db.LegalEntities.Add(new LegalEntity
        {
            Id = entityId,
            TenantId = tenantId,
            Code = "TEST",
            Name = "Test Entity",
            FunctionalCurrency = "MYR",
            CreatedAtUtc = now,
        });

        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = userId,
            TenantId = tenantId,
            Email = $"{userId:N}@example.test",
            DisplayName = "Test User",
            CreatedAtUtc = now,
        });

        var heading = NewAccount(tenantId, "1200", "Current Assets", AccountType.Asset, postable: false);
        var cash = NewAccount(tenantId, "1230", "Cash and Bank", AccountType.Asset);
        var receivables = NewAccount(
            tenantId, "1210", "Trade Receivables", AccountType.Asset,
            control: ControlType.AccountsReceivable);
        var sales = NewAccount(tenantId, "4010", "Sales", AccountType.Income);
        db.Accounts.AddRange(heading, cash, receivables, sales);

        var fiscalYearId = Guid.NewGuid();
        db.FiscalYears.Add(new FiscalYear
        {
            Id = fiscalYearId,
            TenantId = tenantId,
            LegalEntityId = entityId,
            Code = "FY2026",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            State = PeriodState.Open,
        });

        var openPeriodId = Guid.NewGuid();
        var closedPeriodId = Guid.NewGuid();

        db.Periods.Add(new AccountingPeriod
        {
            Id = openPeriodId,
            TenantId = tenantId,
            LegalEntityId = entityId,
            FiscalYearId = fiscalYearId,
            Sequence = 8,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            State = PeriodState.Open,
        });

        db.Periods.Add(new AccountingPeriod
        {
            Id = closedPeriodId,
            TenantId = tenantId,
            LegalEntityId = entityId,
            FiscalYearId = fiscalYearId,
            Sequence = 1,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31),
            State = PeriodState.HardClosed,
        });

        await db.SaveChangesAsync();

        return new LedgerWorld(
            tenantId, entityId, userId, openPeriodId, closedPeriodId,
            cash.Id, sales.Id, receivables.Id, heading.Id);
    }

    private static Account NewAccount(
        Guid tenantId, string code, string name, AccountType type,
        bool postable = true, ControlType control = ControlType.None) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            AccountType = type,
            IsPostable = postable,
            ControlType = control,
        };

    /// <summary>An unsaved entry with the given lines attached.</summary>
    public static JournalEntry Entry(
        LedgerWorld world, string entryNo, params (Guid AccountId, PostingDirection Direction, decimal Amount)[] lines)
    {
        var entryId = Guid.NewGuid();

        var entry = new JournalEntry
        {
            Id = entryId,
            TenantId = world.TenantId,
            LegalEntityId = world.EntityId,
            EntryNo = entryNo,
            EntryDate = new DateOnly(2026, 8, 15),
            PeriodId = world.OpenPeriodId,
            SourceDocumentType = "Manual",
            PostedAtUtc = DateTimeOffset.UtcNow,
            PostedByUserId = world.UserId,
        };

        var lineNo = 1;
        foreach (var (accountId, direction, amount) in lines)
        {
            entry.Postings.Add(new Posting
            {
                Id = Guid.NewGuid(),
                TenantId = world.TenantId,
                LegalEntityId = world.EntityId,
                JournalEntryId = entryId,
                LineNo = lineNo++,
                AccountId = accountId,
                Direction = direction,
                Amount = amount,
                CurrencyCode = "MYR",
                FunctionalAmount = amount,
                FxRate = 1m,
            });
        }

        return entry;
    }
}
