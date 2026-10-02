using SleepDiary.Infrastructure.Persistence.Read.Models;
using SleepDiary.Application.Features.Entries.GetSleepEntry;

namespace SleepDiary.Infrastructure.Features.Entries;

/// <summary>
/// Maps the read model <see cref="SleepEntryRow"/> to the API DTO <see cref="SleepEntryDto"/>; shared by the query handlers of diary entries.
/// </summary>
/// <remarks>
/// A one-to-one copy of columns: all values, including the derived durations, were computed and stored by the aggregate on the write side.
/// The owner (<see cref="SleepEntryRow.UserId"/>) is intentionally not exposed.
/// </remarks>
internal static class EntryMapping
{
    /// <summary>Creates the DTO for one entry row.</summary>
    /// <param name="row">A row loaded from <c>SleepDiaryReadDbContext</c>.</param>
    /// <returns>The DTO with the same values.</returns>
    public static SleepEntryDto ToDto(SleepEntryRow row) => new(
        row.Id,
        row.Date,
        row.BedTime,
        row.WakeTime,
        row.SleepLatencyMinutes,
        row.Awakenings,
        row.Quality,
        row.Notes,
        row.TimeInBedMinutes,
        row.SleepMinutes);
}
