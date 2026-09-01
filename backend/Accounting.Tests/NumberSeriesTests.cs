using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Accounting.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Tests;

[Collection(nameof(DatabaseCollection))]
public class NumberSeriesTests
{
    private static readonly DateOnly August = new(2026, 8, 15);

    [Fact]
    public async Task Allocate_UsesTheSeriesFormat()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var numbers = new NumberSeriesService(db);

        var journal = await numbers.AllocateAsync(world.EntityId, "JournalEntry", August);
        var invoice = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", August);

        // Both formats interpolate the document year, because both series reset yearly — a
        // yearly reset without the year in the format reissues last year's numbers.
        Assert.Equal("JV-2026-00001", journal);
        Assert.Equal("IV-2026-00001", invoice);
    }

    [Fact]
    public async Task Allocate_IncrementsWithinAWindow()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var numbers = new NumberSeriesService(db);

        var first = await numbers.AllocateAsync(world.EntityId, "JournalEntry", August);
        await db.SaveChangesAsync();
        var second = await numbers.AllocateAsync(world.EntityId, "JournalEntry", August);
        await db.SaveChangesAsync();

        Assert.Equal("JV-2026-00001", first);
        Assert.Equal("JV-2026-00002", second);
    }

    [Fact]
    public async Task Allocate_RestartsEachYear()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var numbers = new NumberSeriesService(db);

        var y2026 = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", new DateOnly(2026, 12, 31));
        await db.SaveChangesAsync();
        var y2027 = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", new DateOnly(2027, 1, 1));
        await db.SaveChangesAsync();

        // A yearly series keeps a separate counter per year, so both start at 1.
        Assert.Equal("IV-2026-00001", y2026);
        Assert.Equal("IV-2027-00001", y2027);
    }

    /// <summary>
    /// The property that makes a gapless series gapless.
    /// </summary>
    [Fact]
    public async Task Allocate_WhenTheTransactionRollsBack_DoesNotBurnTheNumber()
    {
        var world = await LedgerFixture.CreateAsync();

        string abandoned;
        await using (var db = world.NewAppContext())
        {
            var numbers = new NumberSeriesService(db);
            await using var transaction = await db.Database.BeginTransactionAsync();
            abandoned = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", August);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        // A fresh context, as a fresh request would have: the increment went back with the
        // transaction, so the next document takes the same number rather than leaving a hole.
        await using (var db = world.NewAppContext())
        {
            var numbers = new NumberSeriesService(db);
            var reused = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", August);
            await db.SaveChangesAsync();

            Assert.Equal("IV-2026-00001", abandoned);
            Assert.Equal(abandoned, reused);
        }
    }

    [Fact]
    public async Task Allocate_ConcurrentCallers_NeverShareANumber()
    {
        var world = await LedgerFixture.CreateAsync();

        async Task<string> AllocateInOwnTransactionAsync()
        {
            await using var db = world.NewAppContext();
            var numbers = new NumberSeriesService(db);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var number = await numbers.AllocateAsync(world.EntityId, "SalesInvoice", August);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return number;
        }

        // The row lock makes the second caller wait rather than read a stale counter.
        var results = await Task.WhenAll(
            AllocateInOwnTransactionAsync(),
            AllocateInOwnTransactionAsync(),
            AllocateInOwnTransactionAsync());

        Assert.Equal(3, results.Distinct().Count());
        Assert.Equal(
            ["IV-2026-00001", "IV-2026-00002", "IV-2026-00003"],
            results.OrderBy(r => r).ToArray());
    }

    [Fact]
    public async Task Allocate_WithNoSeriesForTheDocumentType_IsRejected()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var numbers = new NumberSeriesService(db);

        var ex = await Assert.ThrowsAsync<PostingValidationException>(
            () => numbers.AllocateAsync(world.EntityId, "PurchaseOrder", August));

        Assert.Contains("No active number series", ex.Message);
    }

    [Fact]
    public async Task Allocate_IgnoresInactiveSeries()
    {
        var world = await LedgerFixture.CreateAsync();

        await using (var setup = world.NewAppContext())
        {
            var series = await setup.NumberSeries.FirstAsync(s => s.Code == "JV");
            series.IsActive = false;
            await setup.SaveChangesAsync();
        }

        await using var db = world.NewAppContext();
        var numbers = new NumberSeriesService(db);

        await Assert.ThrowsAsync<PostingValidationException>(
            () => numbers.AllocateAsync(world.EntityId, "JournalEntry", August));
    }

    [Fact]
    public async Task PostedEntries_TakeTheirNumbersFromTheSeries()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var user = new Accounting.Api.Data.CurrentUser();
        user.SetUser(world.UserId);
        var service = new PostingService(
            db, user, new NumberSeriesService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PostingService>.Instance);

        var first = await service.PostAsync(new PostJournalEntryRequest(
            world.EntityId, August,
            [
                new PostingLineRequest(world.CashAccountId, "Debit", 10m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 10m),
            ]));

        var second = await service.PostAsync(new PostJournalEntryRequest(
            world.EntityId, August,
            [
                new PostingLineRequest(world.CashAccountId, "Debit", 20m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 20m),
            ]));

        Assert.Equal("JV-2026-00001", first.EntryNo);
        Assert.Equal("JV-2026-00002", second.EntryNo);
    }

    [Fact]
    public async Task ARejectedPost_LeavesNoGapInTheSeries()
    {
        var world = await LedgerFixture.CreateAsync();
        await using var db = world.NewAppContext();
        var user = new Accounting.Api.Data.CurrentUser();
        user.SetUser(world.UserId);
        var service = new PostingService(
            db, user, new NumberSeriesService(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PostingService>.Instance);

        // Refused before the number is even reached — validation runs first.
        await Assert.ThrowsAsync<PostingValidationException>(() => service.PostAsync(
            new PostJournalEntryRequest(
                world.EntityId, August,
                [
                    new PostingLineRequest(world.CashAccountId, "Debit", 100m),
                    new PostingLineRequest(world.SalesAccountId, "Credit", 90m),
                ])));

        var accepted = await service.PostAsync(new PostJournalEntryRequest(
            world.EntityId, August,
            [
                new PostingLineRequest(world.CashAccountId, "Debit", 100m),
                new PostingLineRequest(world.SalesAccountId, "Credit", 100m),
            ]));

        Assert.Equal("JV-2026-00001", accepted.EntryNo);
    }
}
