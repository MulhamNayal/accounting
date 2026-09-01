using Accounting.Api.Data;
using Accounting.Api.Exceptions;
using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Accounting.Api.Services;

public interface IPostingService
{
    Task<JournalEntryDetail> PostAsync(PostJournalEntryRequest request, CancellationToken ct = default);

    Task<JournalEntryDetail> ReverseAsync(Guid entryId, string reasonCode, CancellationToken ct = default);

    Task<JournalEntryDetail> GetAsync(Guid entryId, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntrySummary>> ListAsync(
        Guid legalEntityId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<TrialBalance> GetTrialBalanceAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Creates and reverses journal entries.
/// </summary>
/// <remarks>
/// Every rule checked here is also enforced by the database. That duplication is
/// deliberate: the database guarantees correctness, but its errors are terse and arrive
/// after the work is done. This layer exists to reject bad input early with a message a
/// person can act on. If the two ever disagree, the database is right.
/// </remarks>
public sealed class PostingService(
    AccountingDbContext db,
    ICurrentUser currentUser,
    INumberSeriesService numbers,
    ILogger<PostingService> logger,
    TimeProvider? clock = null) : IPostingService
{
    /// <summary>
    /// Where "today" comes from when a reversal has to be dated.
    /// </summary>
    /// <remarks>
    /// Injectable because a reversal's date is a business fact, not an implementation detail:
    /// a test that reverses an entry has to be able to say when it is happening. Reading the
    /// wall clock directly made the suite pass only while the calendar sat inside the test
    /// fixture's open period, and it stopped passing on its own the day that month ended.
    /// </remarks>
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private const string JournalEntryDocumentType = "JournalEntry";

    public async Task<JournalEntryDetail> PostAsync(
        PostJournalEntryRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException(
                "No acting user. An entry that cannot be attributed to someone must not be posted.");

        var entity = await db.LegalEntities.FirstOrDefaultAsync(e => e.Id == request.LegalEntityId, ct)
            ?? throw new NotFoundException($"No entity with id {request.LegalEntityId}.");

        if (request.Lines is null || request.Lines.Count < 2)
        {
            throw new PostingValidationException(
                "An entry needs at least two lines — one debit and one credit.");
        }

        var period = await ResolvePeriodAsync(entity.Id, request.EntryDate, ct);

        var lines = await BuildLinesAsync(request, entity, ct);

        var debits = lines.Where(l => l.Direction == PostingDirection.Debit).Sum(l => l.FunctionalAmount);
        var credits = lines.Where(l => l.Direction == PostingDirection.Credit).Sum(l => l.FunctionalAmount);

        if (debits != credits)
        {
            throw new PostingValidationException(
                $"The entry does not balance: debits {debits:N2} against credits {credits:N2} "
                + $"in {entity.FunctionalCurrency}, a difference of {debits - credits:N2}.");
        }

        // One transaction spans number allocation and the write. The counter row stays
        // locked until commit, so a rolled-back post returns its number rather than burning
        // it — which is the whole point of a gapless series.
        await using var scope = await BeginOrJoinAsync(ct);

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            TenantId = entity.TenantId,
            LegalEntityId = entity.Id,
            EntryNo = await numbers.AllocateAsync(
                entity.Id, JournalEntryDocumentType, request.EntryDate, ct),
            EntryDate = request.EntryDate,
            PeriodId = period.Id,
            SourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType)
                ? "Manual"
                : request.SourceDocumentType,
            SourceDocumentId = request.SourceDocumentId,
            PostedAtUtc = _clock.GetUtcNow(),
            PostedByUserId = userId,
            Memo = request.Memo,
        };

        var lineNo = 1;
        foreach (var line in lines)
        {
            line.Id = Guid.NewGuid();
            line.TenantId = entity.TenantId;
            line.LegalEntityId = entity.Id;
            line.JournalEntryId = entry.Id;
            line.LineNo = lineNo++;
            entry.Postings.Add(line);
        }

        db.JournalEntries.Add(entry);
        await SaveAndCommitAsync(scope, ct);

        logger.LogInformation(
            "Posted {EntryNo} for entity {Entity} with {Lines} lines",
            entry.EntryNo, entity.Code, entry.Postings.Count);

        return await GetAsync(entry.Id, ct);
    }

    public async Task<JournalEntryDetail> ReverseAsync(
        Guid entryId, string reasonCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new PostingValidationException(
                "A reversal must carry a reason. A correction nobody can explain is the first "
                + "thing an auditor asks about.");
        }

        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user; the reversal cannot be attributed.");

        var original = await db.JournalEntries
            .Include(e => e.Postings)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new NotFoundException($"No journal entry with id {entryId}.");

        var alreadyReversed = await db.JournalEntries.AnyAsync(e => e.ReversesEntryId == entryId, ct);
        if (alreadyReversed)
        {
            throw new PostingValidationException(
                $"Entry {original.EntryNo} has already been reversed.");
        }

        // The reversal is dated today, not on the original's date: posting it back into the
        // original period would silently restate a figure that may already have been
        // reported. If today's period is closed, the caller is told rather than worked around.
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var period = await ResolvePeriodAsync(original.LegalEntityId, today, ct);

        await using var scope = await BeginOrJoinAsync(ct);

        var reversal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            TenantId = original.TenantId,
            LegalEntityId = original.LegalEntityId,
            EntryNo = await numbers.AllocateAsync(
                original.LegalEntityId, JournalEntryDocumentType, today, ct),
            EntryDate = today,
            PeriodId = period.Id,
            SourceDocumentType = original.SourceDocumentType,
            SourceDocumentId = original.SourceDocumentId,
            PostedAtUtc = _clock.GetUtcNow(),
            PostedByUserId = userId,
            ReversesEntryId = original.Id,
            ReasonCode = reasonCode,
            Memo = $"Reversal of {original.EntryNo}",
        };

        var lineNo = 1;
        foreach (var source in original.Postings.OrderBy(p => p.LineNo))
        {
            reversal.Postings.Add(new Posting
            {
                Id = Guid.NewGuid(),
                TenantId = source.TenantId,
                LegalEntityId = source.LegalEntityId,
                JournalEntryId = reversal.Id,
                LineNo = lineNo++,
                AccountId = source.AccountId,
                Direction = source.Direction == PostingDirection.Debit
                    ? PostingDirection.Credit
                    : PostingDirection.Debit,
                Amount = source.Amount,
                CurrencyCode = source.CurrencyCode,
                // The original rate is reused, not today's. A reversal must undo exactly what
                // was posted; revaluing it would leave a residue on the FX accounts.
                FunctionalAmount = source.FunctionalAmount,
                FxRate = source.FxRate,
                CustomerId = source.CustomerId,
                SupplierId = source.SupplierId,
                ItemId = source.ItemId,
                LocationId = source.LocationId,
                ProjectId = source.ProjectId,
                AgentId = source.AgentId,
                AreaId = source.AreaId,
                TaxCodeId = source.TaxCodeId,
                IntercompanyEntityId = source.IntercompanyEntityId,
                Description = source.Description,
            });
        }

        db.JournalEntries.Add(reversal);
        await SaveAndCommitAsync(scope, ct);

        return await GetAsync(reversal.Id, ct);
    }

    public async Task<JournalEntryDetail> GetAsync(Guid entryId, CancellationToken ct = default)
    {
        var entry = await db.JournalEntries
            .AsNoTracking()
            .Include(e => e.Postings).ThenInclude(p => p.Account)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new NotFoundException($"No journal entry with id {entryId}.");

        var reversedBy = await db.JournalEntries
            .Where(e => e.ReversesEntryId == entryId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        return new JournalEntryDetail(
            entry.Id,
            entry.EntryNo,
            entry.EntryDate,
            entry.SourceDocumentType,
            entry.Memo,
            entry.PostedAtUtc,
            entry.ReversesEntryId,
            entry.SupersedesEntryId,
            reversedBy,
            entry.ReasonCode,
            entry.Postings.OrderBy(p => p.LineNo).Select(p => new PostingLine(
                p.Id,
                p.LineNo,
                p.AccountId,
                p.Account!.Code,
                p.Account.Name,
                p.Direction.ToString(),
                p.Amount,
                p.CurrencyCode,
                p.FunctionalAmount,
                p.FxRate,
                p.CustomerId,
                p.Description)).ToList());
    }

    public async Task<IReadOnlyList<JournalEntrySummary>> ListAsync(
        Guid legalEntityId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = db.JournalEntries
            .AsNoTracking()
            .Where(e => e.LegalEntityId == legalEntityId);

        if (from is not null) query = query.Where(e => e.EntryDate >= from);
        if (to is not null) query = query.Where(e => e.EntryDate <= to);

        return await query
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.EntryNo)
            .Select(e => new JournalEntrySummary(
                e.Id,
                e.EntryNo,
                e.EntryDate,
                e.SourceDocumentType,
                e.Memo,
                e.Postings.Where(p => p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                e.Postings.Count,
                e.ReversesEntryId != null,
                db.JournalEntries.Any(r => r.ReversesEntryId == e.Id)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Balances per account, derived entirely from postings.
    /// </summary>
    /// <remarks>
    /// There is no stored balance to go stale, and nothing to reconcile — this is the same
    /// data the customer ledger and the control account are both computed from.
    /// </remarks>
    public async Task<TrialBalance> GetTrialBalanceAsync(
        Guid legalEntityId, DateOnly asOf, CancellationToken ct = default)
    {
        var rows = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == legalEntityId && p.JournalEntry!.EntryDate <= asOf)
            .GroupBy(p => new { p.AccountId, p.Account!.Code, p.Account.Name, p.Account.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Code,
                g.Key.Name,
                g.Key.AccountType,
                Debit = g.Where(p => p.Direction == PostingDirection.Debit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
                Credit = g.Where(p => p.Direction == PostingDirection.Credit)
                    .Sum(p => (decimal?)p.FunctionalAmount) ?? 0m,
            })
            .ToListAsync(ct);

        var lines = rows
            .OrderBy(r => r.Code)
            .Select(r => new TrialBalanceLine(
                r.AccountId, r.Code, r.Name, r.AccountType.ToString(), r.Debit, r.Credit,
                r.Debit - r.Credit))
            .ToList();

        return new TrialBalance(
            asOf,
            lines,
            lines.Sum(l => l.Debit),
            lines.Sum(l => l.Credit));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<AccountingPeriod> ResolvePeriodAsync(
        Guid legalEntityId, DateOnly date, CancellationToken ct)
    {
        var period = await db.Periods
            .FirstOrDefaultAsync(
                p => p.LegalEntityId == legalEntityId && p.StartDate <= date && p.EndDate >= date, ct)
            ?? throw new PostingValidationException(
                $"No accounting period covers {date:yyyy-MM-dd}. Create the fiscal year first.");

        if (period.State != PeriodState.Open)
        {
            throw new PostingValidationException(
                $"The period covering {date:yyyy-MM-dd} is {period.State} and does not accept postings.");
        }

        return period;
    }

    private async Task<List<Posting>> BuildLinesAsync(
        PostJournalEntryRequest request, LegalEntity entity, CancellationToken ct)
    {
        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var lines = new List<Posting>();

        foreach (var line in request.Lines)
        {
            if (!accounts.TryGetValue(line.AccountId, out var account))
            {
                throw new NotFoundException($"No account with id {line.AccountId}.");
            }

            if (!account.IsPostable)
            {
                throw new PostingValidationException(
                    $"Account {account.Code} ({account.Name}) is a heading. Post to one of its children.");
            }

            if (line.Amount <= 0)
            {
                throw new PostingValidationException(
                    $"Line for account {account.Code} has amount {line.Amount}. Amounts are always "
                    + "positive; the side is set by Direction.");
            }

            if (!Enum.TryParse<PostingDirection>(line.Direction, ignoreCase: true, out var direction))
            {
                throw new PostingValidationException(
                    $"'{line.Direction}' is not a direction. Use Debit or Credit.");
            }

            RequireControlDimension(account, line);

            var currency = string.IsNullOrWhiteSpace(line.CurrencyCode)
                ? entity.FunctionalCurrency
                : line.CurrencyCode.ToUpperInvariant();

            var rate = line.FxRate ?? (currency == entity.FunctionalCurrency ? 1m : 0m);

            if (rate <= 0)
            {
                throw new PostingValidationException(
                    $"Line for account {account.Code} is in {currency} but no exchange rate was given.");
            }

            lines.Add(new Posting
            {
                AccountId = account.Id,
                Direction = direction,
                Amount = line.Amount,
                CurrencyCode = currency,
                FunctionalAmount = decimal.Round(line.Amount * rate, 4, MidpointRounding.ToEven),
                FxRate = rate,
                CustomerId = line.CustomerId,
                SupplierId = line.SupplierId,
                ItemId = line.ItemId,
                ProjectId = line.ProjectId,
                AgentId = line.AgentId,
                AreaId = line.AreaId,
                TaxCodeId = line.TaxCodeId,
                IntercompanyEntityId = line.IntercompanyEntityId,
                Description = line.Description,
            });
        }

        return lines;
    }

    private static void RequireControlDimension(Account account, PostingLineRequest line)
    {
        var missing = account.ControlType switch
        {
            ControlType.AccountsReceivable when line.CustomerId is null => "a customer",
            ControlType.AccountsPayable when line.SupplierId is null => "a supplier",
            ControlType.Stock when line.ItemId is null => "an item",
            _ => null,
        };

        if (missing is not null)
        {
            throw new PostingValidationException(
                $"Account {account.Code} ({account.Name}) is a control account, so the line must "
                + $"name {missing}. Without it the balance is invisible to the subledger while "
                + "still counting toward the control account.");
        }
    }

    /// <summary>
    /// Joins the caller's transaction if there is one, otherwise starts and owns a new one.
    /// </summary>
    /// <remarks>
    /// A document service needs the entry and its own "posted" flag written atomically —
    /// a journal entry with no document pointing at it, or a document marked posted with no
    /// entry behind it, are both worse than a clean failure. So when a caller has already
    /// opened a transaction this joins it and leaves the commit to them.
    /// </remarks>
    private async Task<LedgerTransactionScope> BeginOrJoinAsync(CancellationToken ct)
    {
        var ambient = db.Database.CurrentTransaction;

        return ambient is not null
            ? new LedgerTransactionScope(ambient, owned: false)
            : new LedgerTransactionScope(await db.Database.BeginTransactionAsync(ct), owned: true);
    }

    private sealed class LedgerTransactionScope(IDbContextTransaction transaction, bool owned)
        : IAsyncDisposable
    {
        public Task CommitAsync(CancellationToken ct) =>
            owned ? transaction.CommitAsync(ct) : Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            owned ? transaction.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Writes the entry and commits, translating a database refusal into something readable.
    /// </summary>
    /// <remarks>
    /// The commit is inside the try for a reason: the balance constraint is deferred, so it
    /// fires at COMMIT rather than at SaveChanges. Wrapping only the save would let the one
    /// failure this design cares about most escape as an unhandled 500.
    /// <para>
    /// Reaching here at all means the service's own checks missed a case the database
    /// caught — a race, or a rule enforced in only one place. Worth surfacing distinctly.
    /// </para>
    /// </remarks>
    private async Task SaveAndCommitAsync(LedgerTransactionScope scope, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await scope.CommitAsync(ct);
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException pg)
        {
            logger.LogWarning(ex, "The database refused a post: {Message}", pg.MessageText);
            throw new LedgerIntegrityException(
                $"The ledger refused this entry: {pg.MessageText}", ex);
        }
    }
}
