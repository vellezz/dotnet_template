using SuperApp.Framework.Application.Time;
using Example.Bff.Clients.SleepDiary;
using Refit;

namespace Example.Bff.Summary;

/// <summary>The seven-day sleep period shared by the summary screen and the internal sleep widget.</summary>
/// <remarks>
/// The period ends today in UTC and covers the six days before. The averages are computed by the SleepDiary service over the same period
/// (<c>GET /v1/entries?from=…&amp;to=…</c>); the BFF only reshapes them.
/// </remarks>
internal static class SleepWeek
{
    /// <summary>Asks the SleepDiary service for the user's entries of the last seven days.</summary>
    /// <param name="sleepDiary">Client of the SleepDiary service (the user's token is passed on).</param>
    /// <param name="clock">Source of today's date.</param>
    /// <param name="cancellationToken">Cancellation of the call.</param>
    /// <returns>The service's answer for the period.</returns>
    public static Task<IApiResponse<SleepDiaryDto>> LoadAsync(ISleepDiaryApi sleepDiary, IClock clock, CancellationToken cancellationToken)
    {
        var to = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return sleepDiary.SleepEntriesListAsync(to.AddDays(-6), to, cancellationToken);
    }

    /// <summary>Reshapes the service's diary into the summary of the period.</summary>
    /// <param name="diary">The diary returned by the SleepDiary service.</param>
    /// <returns>The period, the number of entries and the averages.</returns>
    public static SleepWeekDto ToDto(SleepDiaryDto diary) =>
        new(diary.From, diary.To, diary.Entries.Count, diary.AverageSleepMinutes, diary.AverageQuality);
}
