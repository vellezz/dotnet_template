using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Infrastructure.Api;
using Example.Bff.Clients.SleepDiary;
using Example.Bff.Security;
using Example.Bff.Summary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Example.Bff.Controllers.Internal;

/// <summary>
/// Internal API of the Example experience: data of this experience prepared for BFFs of other experiences, e.g. a dashboard showing an
/// Example widget (ADR-0039).
/// </summary>
/// <remarks>
/// <para>
/// Never routed by the edge gateway; reachable only inside the cluster from BFFs of other experiences (NetworkPolicy, ADR-0041). Calls are
/// made in the user's context: the calling BFF passes the user's token, which must carry <c>example.internal.read</c>
/// (<see cref="ExampleBffScopes.InternalRead"/>); the domain service still applies its own resource rules, so a caller only ever gets the
/// data of the user whose token it passes (ADR-0040).
/// </para>
/// <para>
/// The contract is <c>openapi/Example.Bff_internal.json</c>. Consumers belong to other teams: changes must be backward compatible; a breaking
/// change means a new <c>/internal/v{n}</c> version with an agreed end of the old one.
/// </para>
/// </remarks>
/// <param name="sleepDiary">Client of the SleepDiary service.</param>
/// <param name="clock">Source of today's date.</param>
[ApiController]
[Authorize(Policy = ExampleBffScopes.InternalRead)]
[Tags("Internal – Widgets")]
public sealed class InternalWidgetsController(ISleepDiaryApi sleepDiary, IClock clock) : ControllerBase
{
    /// <summary>Returns the user's sleep summary of the last seven days, for a widget in another experience.</summary>
    /// <remarks>
    /// Requires <c>example.internal.read</c> in the user's token passed by the calling BFF, and <c>sleepdiary.entry.read</c> for the SleepDiary
    /// service. The period ends today in UTC and covers the six days before.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>The summary, or the SleepDiary service's error relayed unchanged.</returns>
    /// <response code="200">The period, the number of days with an entry and the averages (<c>null</c> without entries).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks <c>example.internal.read</c>, or the SleepDiary service rejected it (<c>auth.missing_scope</c>, <c>auth.unauthenticated</c>).
    /// </response>
    [HttpGet("internal/v1/widgets/sleep-summary")]
    [ProducesResponseType<SleepWeekDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> SleepSummary(CancellationToken cancellationToken) =>
        this.ToActionResult(await SleepWeek.LoadAsync(sleepDiary, clock, cancellationToken), diary => Ok(SleepWeek.ToDto(diary)));
}
