using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

internal static class RouteNames
{
    public const string GetJournalEntry = "GetJournalEntry";
}

/// <summary>
/// The ledger. Entries are created and reversed; they are never edited or deleted, and no
/// endpoint offers to.
/// </summary>
/// <remarks>
/// No try/catch anywhere here. Failures are typed exceptions from the service, mapped to
/// status codes by GlobalExceptionHandler.
/// </remarks>
[ApiController]
[Route("api/journal-entries")]
public class JournalEntriesController(IPostingService postings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JournalEntrySummary>>> ListAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        return Ok(await postings.ListAsync(entityId, from, to, cancellationToken));
    }

    // Named explicitly: ASP.NET Core strips the "Async" suffix from action names, so
    // nameof(GetAsync) would not resolve — and CreatedAtAction throws after the entry has
    // already been committed, turning a success into a misleading error.
    [HttpGet("{id:guid}", Name = RouteNames.GetJournalEntry)]
    public async Task<ActionResult<JournalEntryDetail>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await postings.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<JournalEntryDetail>> PostAsync(
        [FromBody] PostJournalEntryRequest request, CancellationToken cancellationToken)
    {
        var entry = await postings.PostAsync(request, cancellationToken);
        return CreatedAtRoute(RouteNames.GetJournalEntry, new { id = entry.Id }, entry);
    }

    /// <summary>
    /// Reverses an entry by posting its mirror image. The original is left untouched —
    /// there is no mechanism here or anywhere else to alter it.
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    public async Task<ActionResult<JournalEntryDetail>> ReverseAsync(
        Guid id, [FromBody] ReverseEntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.ReasonCode))
        {
            return BadRequest("A reason is required to reverse an entry.");
        }

        return Ok(await postings.ReverseAsync(id, request.ReasonCode, cancellationToken));
    }
}

[ApiController]
[Route("api/trial-balance")]
public class TrialBalanceController(IPostingService postings) : ControllerBase
{
    /// <summary>
    /// Every account's balance, computed from postings. Nothing is stored, so nothing can
    /// disagree with the ledger.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TrialBalance>> GetAsync(
        [FromQuery] Guid entityId,
        [FromQuery] DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        if (entityId == Guid.Empty)
        {
            return BadRequest("entityId is required.");
        }

        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await postings.GetTrialBalanceAsync(entityId, date, cancellationToken));
    }
}
