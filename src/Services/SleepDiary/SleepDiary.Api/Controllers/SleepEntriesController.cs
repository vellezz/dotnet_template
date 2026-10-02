using SuperApp.Framework.Infrastructure.Api;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using SleepDiary.Application.Features.Entries.DeleteSleepEntry;
using SleepDiary.Application.Features.Entries.GetSleepEntry;
using SleepDiary.Application.Features.Entries.ListSleepEntries;
using SleepDiary.Application.Features.Entries.RecordSleepEntry;
using SleepDiary.Application.Features.Entries.UpdateSleepEntry;

namespace SleepDiary.Api.Controllers;

/// <summary>
/// The caller's sleep diary: at most one entry per day, where the day is the date the user woke up (the night from 29 to 30 September
/// is the entry <c>2026-09-30</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint requires a bearer access token for the SleepDiary API audience; the required scope is listed per endpoint
/// (<c>sleepdiary.entry.read</c> or <c>sleepdiary.entry.write</c>). Users only ever see and change their own entries, identified by the
/// token's <c>sub</c> claim; another user's entry is reported as not found. The scope is checked before the request is validated,
/// so a caller without the scope gets <c>403</c> even for an invalid request.
/// </para>
/// <para>
/// Formats: dates as <c>YYYY-MM-DD</c>; times as the user's local date-time without an offset (<c>2026-09-29T23:15:00</c>), returned exactly
/// as sent; durations in whole minutes.
/// </para>
/// <para>
/// Errors are returned as <c>application/problem+json</c> with the extensions <c>code</c> (stable, machine-readable error code such as
/// <c>sleepdiary.entry.not_found</c>) and <c>traceId</c> (for support requests). Invalid field values detected before the business rules
/// use the code <c>validation.failed</c> and list the invalid fields in <c>errors</c>.
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
[ApiController]
[Route("v1/entries")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class SleepEntriesController(ISender sender) : ControllerBase
{
    /// <summary>Returns the caller's diary entries in a date range, with the average sleep time and quality.</summary>
    /// <remarks>
    /// Required scope: <c>sleepdiary.entry.read</c>. The range is inclusive on both ends, matched against the entry date, and may cover at most
    /// 366 days. Days without an entry are omitted and do not affect the averages.
    /// </remarks>
    /// <param name="from">First day of the range (inclusive), <c>YYYY-MM-DD</c>.</param>
    /// <param name="to">Last day of the range (inclusive), <c>YYYY-MM-DD</c>; not earlier than <paramref name="from"/> and at most 365 days after it.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>The echoed range, the entries sorted by date ascending, the average sleep minutes (1 decimal) and the average quality (2 decimals).</returns>
    /// <response code="200">The diary for the range; <c>entries</c> is empty and both averages are <c>null</c> when there are no entries.</response>
    /// <response code="400">
    /// Invalid range (<c>validation.failed</c>): <paramref name="to"/> is before <paramref name="from"/> or the range is longer than 366 days.
    /// A date that is not in the <c>YYYY-MM-DD</c> format is rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>sleepdiary.entry.read</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).
    /// </response>
    [HttpGet]
    [ProducesResponseType<SleepDiaryDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<IActionResult> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new ListSleepEntries(from, to), cancellationToken));

    /// <summary>Returns the caller's diary entry for one day.</summary>
    /// <remarks>Required scope: <c>sleepdiary.entry.read</c>.</remarks>
    /// <param name="date">Entry date (the day the user woke up), <c>YYYY-MM-DD</c>.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>The entry with the data entered by the user and the computed <c>timeInBedMinutes</c> and <c>sleepMinutes</c>.</returns>
    /// <response code="200">The entry for the day.</response>
    /// <response code="400">The date is not in the <c>YYYY-MM-DD</c> format.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>sleepdiary.entry.read</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="404">The caller has no entry for this day (<c>sleepdiary.entry.not_found</c>).</response>
    [HttpGet("{date}")]
    [ProducesResponseType<SleepEntryDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Get(DateOnly date, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new GetSleepEntry(date), cancellationToken));

    /// <summary>Records a new diary entry for the given day.</summary>
    /// <remarks>
    /// Required scope: <c>sleepdiary.entry.write</c>. One entry per day: to change an existing entry use <c>PUT</c>. The date may be at most
    /// one day after today in UTC (tolerance for time zones ahead of UTC). Time in bed and sleep time (time in bed minus the time to fall asleep)
    /// are computed by the service. Recording an entry publishes the integration event <c>SleepEntryRecordedV1</c> for other services.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="date">Entry date (the day the user woke up), <c>YYYY-MM-DD</c>.</param>
    /// <param name="request">The night's data.</param>
    /// <returns>The identifier of the new entry.</returns>
    /// <response code="201">The entry was recorded; the body contains its identifier.</response>
    /// <response code="400">
    /// Invalid data. <c>validation.failed</c>: quality outside 1–5, awakenings outside 0–50, negative time to fall asleep, note longer than
    /// 2000 characters. A malformed date or body is rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044).
    /// Business rules: <c>sleepdiary.entry.future_date</c> (date too far in the future),
    /// <c>sleepdiary.entry.wake_before_bed</c> (wake-up not after bedtime), <c>sleepdiary.entry.wake_date_mismatch</c> (wake-up day differs
    /// from the entry date), <c>sleepdiary.entry.too_long</c> (more than 24 hours in bed), <c>sleepdiary.entry.invalid_latency</c>
    /// (time to fall asleep longer than the time in bed).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>sleepdiary.entry.write</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="409">The caller already has an entry for this day (<c>sleepdiary.entry.already_exists</c>).</response>
    [HttpPost("{date}")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Record(DateOnly date, SleepEntryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await sender.Send(
                new RecordSleepEntry(date, request.BedTime, request.WakeTime, request.SleepLatencyMinutes, request.Awakenings, request.Quality, request.Notes),
                cancellationToken),
            id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));

    /// <summary>Replaces all data of the caller's existing diary entry for the given day.</summary>
    /// <remarks>
    /// Required scope: <c>sleepdiary.entry.write</c>. A full replacement: every field is overwritten and an omitted or blank note removes the
    /// existing one. The entry date cannot be changed; delete the entry and record a new one instead. Computed values are recalculated.
    /// No integration event is published. When the request is rejected the entry stays unchanged.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="date">Entry date (the day the user woke up), <c>YYYY-MM-DD</c>.</param>
    /// <param name="request">The new data of the night.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The entry was updated.</response>
    /// <response code="400">
    /// Invalid data. <c>validation.failed</c>: quality outside 1–5, awakenings outside 0–50, negative time to fall asleep, note longer than
    /// 2000 characters. A malformed date or body is rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044). Business rules: <c>sleepdiary.entry.wake_before_bed</c>,
    /// <c>sleepdiary.entry.wake_date_mismatch</c>, <c>sleepdiary.entry.too_long</c>, <c>sleepdiary.entry.invalid_latency</c>.
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>sleepdiary.entry.write</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="404">The caller has no entry for this day (<c>sleepdiary.entry.not_found</c>).</response>
    /// <response code="409">
    /// The entry was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{date}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Update(DateOnly date, SleepEntryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(
            new UpdateSleepEntry(date, request.BedTime, request.WakeTime, request.SleepLatencyMinutes, request.Awakenings, request.Quality, request.Notes),
            cancellationToken));

    /// <summary>Permanently deletes the caller's diary entry for the given day.</summary>
    /// <remarks>
    /// Required scope: <c>sleepdiary.entry.write</c>. A new entry can be recorded for the same day afterwards. Other services are not notified.
    /// </remarks>
    /// <param name="date">Entry date (the day the user woke up), <c>YYYY-MM-DD</c>.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The entry was deleted.</response>
    /// <response code="400">The date is not in the <c>YYYY-MM-DD</c> format.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>sleepdiary.entry.write</c> (<c>auth.missing_scope</c>) or does not identify a user (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="404">The caller has no entry for this day (<c>sleepdiary.entry.not_found</c>).</response>
    /// <response code="409">
    /// The entry was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpDelete("{date}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Delete(DateOnly date, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new DeleteSleepEntry(date), cancellationToken));
}
