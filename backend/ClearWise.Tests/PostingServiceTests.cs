using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearWise.Tests;

[Collection(nameof(DatabaseCollection))]
public class PostingServiceTests
{
    private static (PostingService Service, ClearWiseDbContext Db) ServiceFor(LedgerWorld world)
    {
        var db = world.NewAppContext();
        var user = new CurrentUser();
        user.SetUser(world.UserId);
        var numbers = new NumberSeriesService(db);
        return (new PostingService(db, user, numbers, NullLogger<PostingService>.Instance), db);
    }

    private static PostJournalEntryRequest SimpleEntry(
        LedgerWorld world, decimal debit, decimal credit, DateOnly? date = null) => new(
            world.EntityId,
            date ?? new DateOnly(2026, 8, 15),
            [
                new PostingLineRequest(world.CashAccountId, "Debit", debit),
                new PostingLineRequest(world.SalesAccountId, "Credit", credit),
            ],
            Memo: "Test");

    [Fact]
    public async Task Post_BalancedEntry_ReturnsDetailWithBothLines()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var entry = await service.PostAsync(SimpleEntry(world, 1000m, 1000m));

        Assert.StartsWith("JV-", entry.EntryNo);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(1, entry.Lines[0].LineNo);
        Assert.Equal("Debit", entry.Lines[0].Direction);
        Assert.Null(entry.ReversesEntryId);
    }

    [Fact]
    public async Task Post_UnbalancedEntry_IsRejectedBeforeTouchingTheDatabase()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => service.PostAsync(SimpleEntry(world, 1000m, 900m)));

        // The service says which way and by how much; the database would only say it failed.
        Assert.Contains("does not balance", ex.Message);
        Assert.Contains("100.00", ex.Message);
    }

    [Fact]
    public async Task Post_SingleLine_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var request = new PostJournalEntryRequest(
            world.EntityId,
            new DateOnly(2026, 8, 15),
            [new PostingLineRequest(world.CashAccountId, "Debit", 100m)]);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(() => service.PostAsync(request));
        Assert.Contains("at least two lines", ex.Message);
    }

    [Fact]
    public async Task Post_ToHeadingAccount_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var request = new PostJournalEntryRequest(
            world.EntityId,
            new DateOnly(2026, 8, 15),
            [
                new PostingLineRequest(world.HeadingAccountId, "Debit", 100m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 100m),
            ]);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(() => service.PostAsync(request));
        Assert.Contains("heading", ex.Message);
    }

    [Fact]
    public async Task Post_ToControlAccountWithoutItsDimension_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var request = new PostJournalEntryRequest(
            world.EntityId,
            new DateOnly(2026, 8, 15),
            [
                new PostingLineRequest(world.ReceivablesAccountId, "Debit", 100m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 100m),
            ]);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(() => service.PostAsync(request));
        Assert.Contains("must name a customer", ex.Message);
    }

    [Fact]
    public async Task Post_IntoAClosedPeriod_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => service.PostAsync(SimpleEntry(world, 100m, 100m, new DateOnly(2026, 1, 15))));

        Assert.Contains("HardClosed", ex.Message);
    }

    [Fact]
    public async Task Post_WithNoPeriodCoveringTheDate_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => service.PostAsync(SimpleEntry(world, 100m, 100m, new DateOnly(2031, 5, 5))));

        Assert.Contains("No accounting period", ex.Message);
    }

    [Fact]
    public async Task Post_ForeignCurrencyWithoutARate_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var request = new PostJournalEntryRequest(
            world.EntityId,
            new DateOnly(2026, 8, 15),
            [
                new PostingLineRequest(world.CashAccountId, "Debit", 100m, CurrencyCode: "USD"),
                new PostingLineRequest(world.SalesAccountId, "Credit", 100m, CurrencyCode: "USD"),
            ]);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(() => service.PostAsync(request));
        Assert.Contains("no exchange rate", ex.Message);
    }

    [Fact]
    public async Task Post_ForeignCurrency_BalancesInFunctionalCurrency()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        // 100 USD at 4.7 is 470 MYR. The entry balances in MYR, which is the only currency
        // the two sides can meaningfully be compared in.
        var request = new PostJournalEntryRequest(
            world.EntityId,
            new DateOnly(2026, 8, 15),
            [
                new PostingLineRequest(world.CashAccountId, "Debit", 100m, "USD", 4.7m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 470m),
            ]);

        var entry = await service.PostAsync(request);

        Assert.Equal(100m, entry.Lines[0].Amount);
        Assert.Equal(470m, entry.Lines[0].FunctionalAmount);
        Assert.Equal("USD", entry.Lines[0].CurrencyCode);
        Assert.Equal(470m, entry.Lines[1].FunctionalAmount);
    }

    [Fact]
    public async Task Reverse_MirrorsEveryLineAndLinksBack()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var original = await service.PostAsync(SimpleEntry(world, 750m, 750m));
        var reversal = await service.ReverseAsync(original.Id, "Keyed in error");

        Assert.Equal(original.Id, reversal.ReversesEntryId);
        Assert.Equal("Keyed in error", reversal.ReasonCode);

        // Same accounts and amounts, opposite sides.
        Assert.Equal("Credit", reversal.Lines[0].Direction);
        Assert.Equal("Debit", reversal.Lines[1].Direction);
        Assert.Equal(original.Lines[0].AccountCode, reversal.Lines[0].AccountCode);
        Assert.Equal(original.Lines[0].FunctionalAmount, reversal.Lines[0].FunctionalAmount);

        // The original is untouched and now knows it was reversed.
        var refreshed = await service.GetAsync(original.Id);
        Assert.Equal(reversal.Id, refreshed.ReversedByEntryId);
        Assert.Null(refreshed.ReversesEntryId);
    }

    [Fact]
    public async Task Reverse_Twice_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var original = await service.PostAsync(SimpleEntry(world, 200m, 200m));
        await service.ReverseAsync(original.Id, "First");

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => service.ReverseAsync(original.Id, "Second"));

        Assert.Contains("already been reversed", ex.Message);
    }

    [Fact]
    public async Task Reverse_WithoutAReason_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var original = await service.PostAsync(SimpleEntry(world, 200m, 200m));

        await Assert.ThrowsAsync<PostingValidationException>(
            () => service.ReverseAsync(original.Id, "   "));
    }

    [Fact]
    public async Task Reverse_AnEntryThatDoesNotExist_IsNotFound()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ReverseAsync(Guid.NewGuid(), "Reason"));
    }

    [Fact]
    public async Task TrialBalance_AlwaysBalances_AndAReversedPairNetsToNothing()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        await service.PostAsync(SimpleEntry(world, 1000m, 1000m));
        var second = await service.PostAsync(SimpleEntry(world, 400m, 400m));
        await service.ReverseAsync(second.Id, "Keyed in error");

        var trialBalance = await service.GetTrialBalanceAsync(world.EntityId, new DateOnly(2026, 12, 31));

        Assert.True(trialBalance.IsBalanced);
        Assert.Equal(trialBalance.TotalDebit, trialBalance.TotalCredit);

        // 1000 remains; the 400 pair cancels.
        var cash = trialBalance.Lines.Single(l => l.AccountId == world.CashAccountId);
        Assert.Equal(1000m, cash.Balance);
    }

    [Fact]
    public async Task List_ShowsWhichEntriesAreReversalsAndWhichWereReversed()
    {
        var world = await LedgerFixture.CreateAsync();
        var (service, db) = ServiceFor(world);
        await using var _ = db;

        var original = await service.PostAsync(SimpleEntry(world, 300m, 300m));
        var reversal = await service.ReverseAsync(original.Id, "Keyed in error");

        var entries = await service.ListAsync(world.EntityId, null, null);

        Assert.Equal(2, entries.Count);
        Assert.True(entries.Single(e => e.Id == original.Id).IsReversed);
        Assert.False(entries.Single(e => e.Id == original.Id).IsReversal);
        Assert.True(entries.Single(e => e.Id == reversal.Id).IsReversal);
    }
}
