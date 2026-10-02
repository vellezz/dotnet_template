using SuperApp.Framework.Infrastructure.Http.Downstream;
using SuperApp.Framework.Application.Time;
using Example.Bff.Clients.Knowledge;
using Example.Bff.Clients.SleepDiary;
using Example.Bff.Summary;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Example.Bff.Controllers.Experience;

/// <summary>
/// Endpoints of the Example experience that do not exist in any single domain service: the BFF composes them from several services
/// (ADR-0038).
/// </summary>
/// <remarks>
/// This is the part of a BFF that justifies its existence beyond passing calls on: one request from the module instead of several, data shaped
/// for the screen, and partial rendering when one of the services is slow or down (<c>PartialResponseFetcher</c>).
/// </remarks>
/// <param name="knowledge">Client of the Knowledge service.</param>
/// <param name="sleepDiary">Client of the SleepDiary service.</param>
/// <param name="clock">Source of today's date.</param>
/// <param name="logger">Logger of parts that could not be fetched (event ID 6001).</param>
[ApiController]
[Tags("Example – Summary")]
public sealed partial class ExperienceSummaryController(
    IKnowledgeApi knowledge,
    ISleepDiaryApi sleepDiary,
    IClock clock,
    ILogger<ExperienceSummaryController> logger) : ControllerBase
{
    private const int LatestFavorites = 5;

    /// <summary>Returns the start screen of the module: the user's latest favorites and sleep in the last seven days.</summary>
    /// <remarks>
    /// Calls the Knowledge service (<c>knowledge.library.read</c>) and the SleepDiary service (<c>sleepdiary.entry.read</c>) at the same time,
    /// each with a 2-second limit. The answer is always 200: every part carries its own <c>status</c> (<c>Ok</c>, <c>Forbidden</c>,
    /// <c>Unavailable</c>, <c>Timeout</c>) and <c>data</c> only when it is <c>Ok</c>, so the module shows the parts it got and a message for
    /// the others.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>The composed summary.</returns>
    /// <response code="200">The summary; check the <c>status</c> of each part.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    [HttpGet("v1/me/summary")]
    [ProducesResponseType<MySummaryDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MySummaryDto>> Get(CancellationToken cancellationToken)
    {
        var favorites = PartialResponseFetcher.FetchAsync(
            token => knowledge.LibraryFavoritesAsync(page: 1, pageSize: LatestFavorites, token),
            page => new FavoritesSummaryDto(
                page.TotalCount,
                [.. page.Items.Select(item => new FavoriteItemDto(item.ItemType.ToString(), item.ItemId, item.Title))]),
            cancellationToken);
        var sleepWeek = PartialResponseFetcher.FetchAsync(
            token => SleepWeek.LoadAsync(sleepDiary, clock, token),
            SleepWeek.ToDto,
            cancellationToken);

        var summary = new MySummaryDto(await favorites, await sleepWeek);
        LogPartIfNotOk(nameof(MySummaryDto.Favorites), summary.Favorites.Status);
        LogPartIfNotOk(nameof(MySummaryDto.SleepWeek), summary.SleepWeek.Status);
        return summary;
    }

    private void LogPartIfNotOk(string part, ResponsePartStatus status)
    {
        if (status != ResponsePartStatus.Ok)
        {
            LogPartNotOk(logger, part, status);
        }
    }

    // Source-generated log method (the only allowed way of logging, see architecture rules §11).
    [LoggerMessage(6001, LogLevel.Warning, "Summary part {Part} returned {Status}")]
    private static partial void LogPartNotOk(ILogger logger, string part, ResponsePartStatus status);
}
