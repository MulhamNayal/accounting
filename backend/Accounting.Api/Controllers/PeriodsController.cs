using Accounting.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/fiscal-years")]
public class FiscalYearsController(
    IFiscalYearService fiscalYears,
    IYearEndCloseService yearEnd) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FiscalYearSummary>>> ListAsync(
        [FromQuery] Guid entityId, CancellationToken cancellationToken)
        => Ok(await fiscalYears.ListAsync(entityId, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetFiscalYear")]
    public async Task<ActionResult<FiscalYearSummary>> GetAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await fiscalYears.GetAsync(id, cancellationToken));

    /// <summary>Creates a year and generates its posting periods.</summary>
    [HttpPost]
    public async Task<ActionResult<FiscalYearSummary>> CreateAsync(
        [FromBody] CreateFiscalYearRequest request, CancellationToken cancellationToken)
    {
        var year = await fiscalYears.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetFiscalYear", new { id = year.Id }, year);
    }

    /// <summary>
    /// What the year's closing entry would post, without posting it.
    /// </summary>
    [HttpGet("{id:guid}/closing-entry")]
    public async Task<ActionResult<ClosingEntryPreview>> PreviewClosingEntryAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await yearEnd.GetPreviewAsync(id, cancellationToken));

    /// <summary>
    /// Transfers the year's income and expenses to retained earnings.
    /// </summary>
    /// <remarks>
    /// An ordinary journal entry, and reversible like any other. The year stays open until it
    /// is finalised separately, which is what makes a late adjustment survivable.
    /// </remarks>
    [HttpPost("{id:guid}/closing-entry")]
    public async Task<ActionResult<JournalEntryDetail>> PostClosingEntryAsync(
        Guid id, [FromBody] PostClosingEntryRequest? request, CancellationToken cancellationToken)
        => Ok(await yearEnd.PostClosingEntryAsync(id, request?.Memo, cancellationToken));

    /// <summary>
    /// Hard closes every period in the year and the year itself.
    /// </summary>
    /// <remarks>
    /// There is no transition out of hard closed anywhere in this model, and no endpoint that
    /// could add one.
    /// </remarks>
    [HttpPost("{id:guid}/finalise")]
    public async Task<ActionResult<FiscalYearSummary>> FinaliseAsync(
        Guid id, [FromBody] FinaliseFiscalYearRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest("A reason is required.");
        }

        return Ok(await yearEnd.FinaliseAsync(id, request.Reason, cancellationToken));
    }
}

[ApiController]
[Route("api/periods")]
public class PeriodsController(IPeriodService periods) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PeriodSummary>>> ListAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid? fiscalYearId,
        CancellationToken cancellationToken)
        => Ok(await periods.ListAsync(entityId, fiscalYearId, cancellationToken));

    /// <summary>Every recorded state transition, newest first.</summary>
    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<PeriodEventSummary>>> EventsAsync(
        [FromQuery] Guid entityId,
        [FromQuery] Guid? fiscalYearId,
        CancellationToken cancellationToken)
        => Ok(await periods.GetEventsAsync(entityId, fiscalYearId, cancellationToken));

    /// <summary>What stands between this period and being closed.</summary>
    [HttpGet("{id:guid}/readiness")]
    public async Task<ActionResult<PeriodReadiness>> ReadinessAsync(
        Guid id, CancellationToken cancellationToken)
        => Ok(await periods.GetReadinessAsync(id, cancellationToken));

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<PeriodSummary>> CloseAsync(
        Guid id, [FromBody] ChangePeriodStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest("A reason is required.");
        }

        return Ok(await periods.SoftCloseAsync(id, request.Reason, cancellationToken));
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<PeriodSummary>> ReopenAsync(
        Guid id, [FromBody] ChangePeriodStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest("A reason is required.");
        }

        return Ok(await periods.ReopenAsync(id, request.Reason, cancellationToken));
    }
}
