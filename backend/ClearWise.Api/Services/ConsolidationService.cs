using ClearWise.Api.Data;
using ClearWise.Api.Exceptions;
using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Services;

public interface IConsolidationService
{
    Task<ConsolidationResult> RunAsync(RunConsolidationRequest request, CancellationToken ct = default);

    Task<ConsolidationResult> GetAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<ConsolidationRunSummary>> ListAsync(CancellationToken ct = default);

    Task<IReadOnlyList<IntercompanyPair>> GetIntercompanyAsync(
        DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Combines the tenant's entities into one set of group figures.
/// </summary>
/// <remarks>
/// Consolidated is not the sum of the entities. Two adjustments stand between them:
/// transactions the group had with itself, which are not group income or expense at all; and
/// translation, when entities keep their books in different currencies.
/// </remarks>
public sealed class ConsolidationService(
    ClearWiseDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ILogger<ConsolidationService> logger) : IConsolidationService
{
    public async Task<ConsolidationResult> RunAsync(
        RunConsolidationRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new PostingValidationException("No acting user.");

        var tenantId = tenantContext.TenantId
            ?? throw new PostingValidationException("No tenant in scope.");

        var presentation = (request.PresentationCurrency ?? string.Empty).Trim().ToUpperInvariant();

        if (presentation.Length != 3)
        {
            throw new PostingValidationException(
                "A presentation currency is required, as a three-letter ISO 4217 code.");
        }

        var entities = await db.LegalEntities
            .Where(e => e.IsActive)
            .OrderBy(e => e.Code)
            .ToListAsync(ct);

        if (entities.Count == 0)
        {
            throw new PostingValidationException("The tenant has no active entities to consolidate.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var run = new ConsolidationRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AsOf = request.AsOf,
            PresentationCurrency = presentation,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Note = request.Note,
        };

        var lines = new List<ConsolidationPosting>();

        foreach (var entity in entities)
        {
            lines.AddRange(await BuildEntityLinesAsync(run, entity, presentation, ct));
        }

        lines.AddRange(await BuildEliminationsAsync(run, presentation, ct));

        // Translation is computed last, from everything above: it is whatever the other
        // lines fail to balance by.
        var residue = await BuildTranslationResidueAsync(run, lines, tenantId, ct);
        if (residue is not null)
        {
            lines.Add(residue);
        }

        db.ConsolidationRuns.Add(run);
        db.ConsolidationPostings.AddRange(lines);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Consolidated {Entities} entities as at {AsOf} in {Currency}",
            entities.Count, request.AsOf, presentation);

        return await GetAsync(run.Id, ct);
    }

    // ---------------------------------------------------------------- entity balances

    /// <summary>
    /// An entity's account balances, translated if its books are in another currency.
    /// </summary>
    /// <remarks>
    /// Balance sheet items are translated at the closing rate because that is what they are
    /// worth on the date. Income and expense are translated at the period average, because
    /// they arose throughout the period rather than on its last day. That the two rates
    /// differ is exactly why translation leaves a residue.
    /// </remarks>
    private async Task<List<ConsolidationPosting>> BuildEntityLinesAsync(
        ConsolidationRun run, LegalEntity entity, string presentation, CancellationToken ct)
    {
        var balances = await db.Postings
            .AsNoTracking()
            .Where(p => p.LegalEntityId == entity.Id && p.JournalEntry!.EntryDate <= run.AsOf)
            .GroupBy(p => new { p.AccountId, p.Account!.AccountType })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountType,
                Net = g.Sum(p => p.Direction == PostingDirection.Debit
                    ? p.FunctionalAmount
                    : -p.FunctionalAmount),
            })
            .Where(x => x.Net != 0)
            .ToListAsync(ct);

        var sameCurrency = entity.FunctionalCurrency == presentation;

        var closing = sameCurrency
            ? 1m
            : await RequireRateAsync(entity.FunctionalCurrency, presentation, run.AsOf, average: false, ct);

        var average = sameCurrency
            ? 1m
            : await RequireRateAsync(entity.FunctionalCurrency, presentation, run.AsOf, average: true, ct);

        return balances
            .Select(b =>
            {
                var rate = IsBalanceSheet(b.AccountType) ? closing : average;
                return NewLine(
                    run, entity.Id, b.AccountId, b.Net,
                    decimal.Round(b.Net * rate, 4, MidpointRounding.ToEven),
                    ConsolidationLineKind.Entity, rate);
            })
            .ToList();
    }

    private static bool IsBalanceSheet(AccountType type) =>
        type is AccountType.Asset or AccountType.Liability or AccountType.Equity;

    // ---------------------------------------------------------------- eliminations

    /// <summary>
    /// Removes transactions the group had with itself.
    /// </summary>
    /// <remarks>
    /// A management fee charged by one entity to another is income to the first and expense
    /// to the second. At group level neither happened — no money left the group — so both
    /// sides are reversed. Leaving them in inflates revenue and costs by the same amount,
    /// which flatters turnover without changing profit and is exactly the kind of thing an
    /// auditor looks for.
    /// <para>
    /// Only postings that name a sister entity are eliminated. That marking is a deliberate
    /// act at posting time, not a guess made here by matching amounts — inferring
    /// intercompany from coincidence would eliminate real third-party trade.
    /// </para>
    /// </remarks>
    private async Task<List<ConsolidationPosting>> BuildEliminationsAsync(
        ConsolidationRun run, string presentation, CancellationToken ct)
    {
        var intercompany = await db.Postings
            .AsNoTracking()
            .Where(p => p.IntercompanyEntityId != null && p.JournalEntry!.EntryDate <= run.AsOf)
            .GroupBy(p => new { p.LegalEntityId, p.AccountId, p.Account!.AccountType })
            .Select(g => new
            {
                g.Key.LegalEntityId,
                g.Key.AccountId,
                g.Key.AccountType,
                Net = g.Sum(p => p.Direction == PostingDirection.Debit
                    ? p.FunctionalAmount
                    : -p.FunctionalAmount),
            })
            .Where(x => x.Net != 0)
            .ToListAsync(ct);

        if (intercompany.Count == 0)
        {
            return [];
        }

        var entityCurrencies = await db.LegalEntities
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => e.FunctionalCurrency, ct);

        var lines = new List<ConsolidationPosting>();

        foreach (var row in intercompany)
        {
            var currency = entityCurrencies[row.LegalEntityId];
            var rate = currency == presentation
                ? 1m
                : await RequireRateAsync(
                    currency, presentation, run.AsOf, !IsBalanceSheet(row.AccountType), ct);

            // Negated: the elimination is the mirror of what the entity recorded.
            lines.Add(NewLine(
                run, row.LegalEntityId, row.AccountId, -row.Net,
                decimal.Round(-row.Net * rate, 4, MidpointRounding.ToEven),
                ConsolidationLineKind.Elimination, rate));
        }

        return lines;
    }

    // ---------------------------------------------------------------- translation

    /// <summary>
    /// Takes whatever the translated figures fail to balance by to the translation reserve.
    /// </summary>
    /// <remarks>
    /// Not an error and not a plug. Translating the balance sheet at one rate and the income
    /// statement at another cannot balance, and IAS 21 puts the difference in a separate
    /// equity reserve rather than in profit — because nobody realised a gain, the rates
    /// simply moved.
    /// </remarks>
    private async Task<ConsolidationPosting?> BuildTranslationResidueAsync(
        ConsolidationRun run,
        IReadOnlyList<ConsolidationPosting> lines,
        Guid tenantId,
        CancellationToken ct)
    {
        var imbalance = lines.Sum(l => l.Direction == PostingDirection.Debit
            ? l.PresentationAmount
            : -l.PresentationAmount);

        imbalance = decimal.Round(imbalance, 4, MidpointRounding.ToEven);

        if (imbalance == 0)
        {
            return null;
        }

        var reserve = await db.Accounts
            .FirstOrDefaultAsync(
                a => a.TenantId == tenantId
                     && a.SystemRole == AccountSystemRole.CurrencyTranslationReserve
                     && a.IsPostable && a.IsActive,
                ct)
            ?? throw new PostingValidationException(
                $"Translating into {run.PresentationCurrency} leaves {imbalance:N2} that must go "
                + "to a currency translation reserve, but no account is marked as one. Mark one "
                + "before consolidating across currencies.");

        // The residue is credited when the translated debits exceed credits, and vice versa.
        return NewLine(
            run, null, reserve.Id, -imbalance, -imbalance,
            ConsolidationLineKind.Translation, 1m);
    }

    // ---------------------------------------------------------------- reading

    public async Task<ConsolidationResult> GetAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.ConsolidationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException($"No consolidation run with id {runId}.");

        var rows = await db.ConsolidationPostings
            .AsNoTracking()
            .Where(p => p.ConsolidationRunId == runId)
            .Select(p => new
            {
                p.AccountId,
                p.Account!.Code,
                p.Account.Name,
                p.Account.AccountType,
                p.LegalEntityId,
                EntityCode = p.LegalEntity == null ? null : p.LegalEntity.Code,
                p.Direction,
                p.PresentationAmount,
                p.Kind,
            })
            .ToListAsync(ct);

        decimal Signed(PostingDirection direction, decimal amount) =>
            direction == PostingDirection.Debit ? amount : -amount;

        var byAccount = rows
            .GroupBy(r => new { r.AccountId, r.Code, r.Name, r.AccountType })
            .Select(g => new ConsolidatedLine(
                g.Key.AccountId,
                g.Key.Code,
                g.Key.Name,
                g.Key.AccountType.ToString(),
                g.Where(r => r.Kind == ConsolidationLineKind.Entity)
                    .Sum(r => Signed(r.Direction, r.PresentationAmount)),
                g.Where(r => r.Kind == ConsolidationLineKind.Elimination)
                    .Sum(r => Signed(r.Direction, r.PresentationAmount)),
                g.Where(r => r.Kind == ConsolidationLineKind.Translation)
                    .Sum(r => Signed(r.Direction, r.PresentationAmount)),
                g.Sum(r => Signed(r.Direction, r.PresentationAmount))))
            .OrderBy(l => l.AccountCode)
            .ToList();

        var contributions = rows
            .Where(r => r.LegalEntityId != null && r.Kind == ConsolidationLineKind.Entity)
            .GroupBy(r => r.EntityCode!)
            .Select(g => new EntityContribution(
                g.Key, g.Sum(r => Signed(r.Direction, r.PresentationAmount))))
            .OrderBy(c => c.EntityCode)
            .ToList();

        var totalDebit = rows.Where(r => r.Direction == PostingDirection.Debit)
            .Sum(r => r.PresentationAmount);
        var totalCredit = rows.Where(r => r.Direction == PostingDirection.Credit)
            .Sum(r => r.PresentationAmount);

        return new ConsolidationResult(
            run.Id,
            run.AsOf,
            run.PresentationCurrency,
            run.CreatedAtUtc,
            run.Note,
            byAccount,
            contributions,
            decimal.Round(totalDebit, 4, MidpointRounding.ToEven),
            decimal.Round(totalCredit, 4, MidpointRounding.ToEven));
    }

    public async Task<IReadOnlyList<ConsolidationRunSummary>> ListAsync(CancellationToken ct = default)
        => await db.ConsolidationRuns
            .AsNoTracking()
            .OrderByDescending(r => r.AsOf)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new ConsolidationRunSummary(
                r.Id, r.AsOf, r.PresentationCurrency, r.CreatedAtUtc, r.Note,
                r.Postings.Count))
            .ToListAsync(ct);

    /// <summary>
    /// Intercompany balances by entity pair, so they can be checked before consolidating.
    /// </summary>
    /// <remarks>
    /// The two sides of an intercompany transaction should net to nothing. When they do not,
    /// one entity has recorded something the other has not, and consolidating first would
    /// bury that difference in the group figures.
    /// </remarks>
    public async Task<IReadOnlyList<IntercompanyPair>> GetIntercompanyAsync(
        DateOnly asOf, CancellationToken ct = default)
    {
        var rows = await db.Postings
            .AsNoTracking()
            .Where(p => p.IntercompanyEntityId != null && p.JournalEntry!.EntryDate <= asOf)
            .Select(p => new
            {
                From = p.LegalEntity!.Code,
                To = p.IntercompanyEntity!.Code,
                Signed = p.Direction == PostingDirection.Debit
                    ? p.FunctionalAmount
                    : -p.FunctionalAmount,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.From, r.To })
            .Select(g => new IntercompanyPair(
                g.Key.From, g.Key.To, g.Sum(r => r.Signed), g.Count()))
            .OrderBy(p => p.FromEntity).ThenBy(p => p.ToEntity)
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private ConsolidationPosting NewLine(
        ConsolidationRun run,
        Guid? entityId,
        Guid accountId,
        decimal signedFunctional,
        decimal signedPresentation,
        ConsolidationLineKind kind,
        decimal rate) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = run.TenantId,
            ConsolidationRunId = run.Id,
            LegalEntityId = entityId,
            AccountId = accountId,
            // Direction carries the sign here as it does in the ledger, so the two can be
            // compared without a special case.
            Direction = signedPresentation >= 0 ? PostingDirection.Debit : PostingDirection.Credit,
            FunctionalAmount = Math.Abs(signedFunctional),
            PresentationAmount = Math.Abs(signedPresentation),
            Kind = kind,
            RateUsed = rate,
        };

    /// <summary>
    /// The most recent rate on or before the date, refusing to guess when there is none.
    /// </summary>
    /// <remarks>
    /// Defaulting to 1 would silently report a foreign entity as though its currency were the
    /// group's — a wrong number that looks entirely plausible. Failing loudly is the only
    /// safe behaviour.
    /// </remarks>
    private async Task<decimal> RequireRateAsync(
        string from, string to, DateOnly asOf, bool average, CancellationToken ct)
    {
        var rate = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.FromCurrency == from && r.ToCurrency == to && r.RateDate <= asOf)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingValidationException(
                $"No exchange rate from {from} to {to} on or before {asOf:yyyy-MM-dd}. "
                + "Consolidating without one would report the entity as though its books were "
                + "already in the group's currency.");

        if (average)
        {
            return rate.AverageRate
                ?? throw new PostingValidationException(
                    $"The {from} to {to} rate for {rate.RateDate:yyyy-MM-dd} has no average "
                    + "rate. Income and expense are translated at the period average, not the "
                    + "closing rate.");
        }

        return rate.ClosingRate;
    }
}
