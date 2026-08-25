using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Tests;

/// <summary>
/// The guarantees Layer 1 exists to provide. Every one of these must be enforced by
/// PostgreSQL, not by the application — a test that passes only because the service layer
/// declined to try would prove nothing.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class LedgerInvariantTests
{
    /// <summary>
    /// Runs an operation expected to be refused and returns the database's message.
    /// </summary>
    /// <remarks>
    /// The exception type is deliberately not asserted. A constraint checked immediately
    /// arrives wrapped in <see cref="DbUpdateException"/>, while one deferred to commit
    /// surfaces from the commit itself and is not wrapped. Both are the database refusing;
    /// pinning the wrapper would make the test about EF's plumbing rather than the
    /// guarantee.
    /// </remarks>
    private static async Task<string> RefusalMessageAsync(Func<Task> operation)
    {
        var exception = await Record.ExceptionAsync(operation);
        Assert.NotNull(exception);
        return exception!.GetBaseException().Message;
    }

    [Fact]
    public async Task BalancedEntry_IsAccepted()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        db.JournalEntries.Add(LedgerFixture.Entry(
            world, "JV-00001",
            (world.CashAccountId, PostingDirection.Debit, 1000m),
            (world.SalesAccountId, PostingDirection.Credit, 1000m)));

        await db.SaveChangesAsync();

        var lines = await db.Postings.CountAsync();
        Assert.Equal(2, lines);
    }

    [Fact]
    public async Task UnbalancedEntry_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        db.JournalEntries.Add(LedgerFixture.Entry(
            world, "JV-00002",
            (world.CashAccountId, PostingDirection.Debit, 1000m),
            (world.SalesAccountId, PostingDirection.Credit, 900m)));

        // The individual inserts are legal; the entry is judged when the transaction
        // commits, which is what DEFERRABLE INITIALLY DEFERRED buys us.
        var message = await RefusalMessageAsync(() => db.SaveChangesAsync());
        Assert.Contains("does not balance", message);
    }

    [Fact]
    public async Task SingleSidedEntry_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        db.JournalEntries.Add(LedgerFixture.Entry(
            world, "JV-00003",
            (world.CashAccountId, PostingDirection.Debit, 500m)));

        var message = await RefusalMessageAsync(() => db.SaveChangesAsync());
        Assert.Contains("at least two", message);
    }

    [Fact]
    public async Task UpdatingAPostedPosting_IsDeniedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();

        await using (var setup = world.NewAppContext())
        {
            setup.JournalEntries.Add(LedgerFixture.Entry(
                world, "JV-00010",
                (world.CashAccountId, PostingDirection.Debit, 250m),
                (world.SalesAccountId, PostingDirection.Credit, 250m)));
            await setup.SaveChangesAsync();
        }

        await using var db = world.NewAppContext();
        var posting = await db.Postings.FirstAsync();
        posting.FunctionalAmount = 999_999m;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Not a validation message from our code — PostgreSQL refusing the statement.
        Assert.Contains("permission denied", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task DeletingAPostedPosting_IsDeniedByTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();

        await using (var setup = world.NewAppContext())
        {
            setup.JournalEntries.Add(LedgerFixture.Entry(
                world, "JV-00011",
                (world.CashAccountId, PostingDirection.Debit, 250m),
                (world.SalesAccountId, PostingDirection.Credit, 250m)));
            await setup.SaveChangesAsync();
        }

        await using var db = world.NewAppContext();
        db.Postings.Remove(await db.Postings.FirstAsync());

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("permission denied", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task PostingToAHeadingAccount_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        db.JournalEntries.Add(LedgerFixture.Entry(
            world, "JV-00004",
            (world.HeadingAccountId, PostingDirection.Debit, 100m),
            (world.SalesAccountId, PostingDirection.Credit, 100m)));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("heading", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task ReceivablesPostingWithoutACustomer_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        // Without this, the posting counts toward the control account while being invisible
        // to the derived subledger — exactly the drift this design exists to prevent.
        db.JournalEntries.Add(LedgerFixture.Entry(
            world, "JV-00005",
            (world.ReceivablesAccountId, PostingDirection.Debit, 100m),
            (world.SalesAccountId, PostingDirection.Credit, 100m)));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("must name a customer", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task ReceivablesPostingWithACustomer_IsAccepted()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        var entry = LedgerFixture.Entry(
            world, "JV-00006",
            (world.ReceivablesAccountId, PostingDirection.Debit, 100m),
            (world.SalesAccountId, PostingDirection.Credit, 100m));

        entry.Postings.First().CustomerId = Guid.NewGuid();

        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Postings.CountAsync());
    }

    [Fact]
    public async Task PostingIntoAClosedPeriod_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        var entry = LedgerFixture.Entry(
            world, "JV-00007",
            (world.CashAccountId, PostingDirection.Debit, 100m),
            (world.SalesAccountId, PostingDirection.Credit, 100m));

        entry.PeriodId = world.ClosedPeriodId;
        entry.EntryDate = new DateOnly(2026, 1, 15);

        db.JournalEntries.Add(entry);

        var message = await RefusalMessageAsync(() => db.SaveChangesAsync());
        Assert.Contains("does not accept postings", message);
    }

    [Fact]
    public async Task EntryDateOutsideItsPeriod_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        // Otherwise a caller sidesteps a closed month by pointing an out-of-range date at
        // an open period.
        var entry = LedgerFixture.Entry(
            world, "JV-00008",
            (world.CashAccountId, PostingDirection.Debit, 100m),
            (world.SalesAccountId, PostingDirection.Credit, 100m));

        entry.EntryDate = new DateOnly(2026, 3, 15); // open period covers August

        db.JournalEntries.Add(entry);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("outside period", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task ReversalWithoutAReason_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();

        Guid originalId;
        await using (var setup = world.NewAppContext())
        {
            var original = LedgerFixture.Entry(
                world, "JV-00020",
                (world.CashAccountId, PostingDirection.Debit, 400m),
                (world.SalesAccountId, PostingDirection.Credit, 400m));
            setup.JournalEntries.Add(original);
            await setup.SaveChangesAsync();
            originalId = original.Id;
        }

        await using var db = world.NewAppContext();

        var reversal = LedgerFixture.Entry(
            world, "JV-00021",
            (world.CashAccountId, PostingDirection.Credit, 400m),
            (world.SalesAccountId, PostingDirection.Debit, 400m));
        reversal.ReversesEntryId = originalId;
        reversal.ReasonCode = null; // the omission under test

        db.JournalEntries.Add(reversal);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_journal_entry_reversal_has_reason", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task Reversal_SumsToZeroWithItsOriginal()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();

        var original = LedgerFixture.Entry(
            world, "JV-00030",
            (world.CashAccountId, PostingDirection.Debit, 750m),
            (world.SalesAccountId, PostingDirection.Credit, 750m));
        db.JournalEntries.Add(original);
        await db.SaveChangesAsync();

        var reversal = LedgerFixture.Entry(
            world, "JV-00031",
            (world.CashAccountId, PostingDirection.Credit, 750m),
            (world.SalesAccountId, PostingDirection.Debit, 750m));
        reversal.ReversesEntryId = original.Id;
        reversal.ReasonCode = "Keyed in error";
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync();

        // The original is untouched; the pair nets to nothing on every account.
        var net = await db.Postings
            .GroupBy(p => p.AccountId)
            .Select(g => g.Sum(p => p.Direction == PostingDirection.Debit
                ? p.FunctionalAmount
                : -p.FunctionalAmount))
            .ToListAsync();

        Assert.All(net, n => Assert.Equal(0m, n));
        Assert.Equal(4, await db.Postings.CountAsync());
    }
}
