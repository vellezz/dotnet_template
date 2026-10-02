using SleepDiary.Application.Features.Entries.GetSleepEntry;

namespace SleepDiary.Application.Features.Entries.ListSleepEntries;

/// <summary>
/// The current user's sleep diary for a range of days: the entries in the range and averages computed from them. Result of <see cref="ListSleepEntries"/>.
/// </summary>
/// <remarks>
/// The averages are computed only over existing entries; days without an entry are ignored rather than counted as zero.
/// </remarks>
/// <param name="From">First day of the range (inclusive), echoed from the query.</param>
/// <param name="To">Last day of the range (inclusive), echoed from the query.</param>
/// <param name="Entries">Entries whose date falls in the range, sorted by date ascending; an empty list when there are none.</param>
/// <param name="AverageSleepMinutes">
/// Mean of <see cref="SleepEntryDto.SleepMinutes"/> over <paramref name="Entries"/>, in minutes, rounded to 1 decimal place;
/// <see langword="null"/> when there are no entries.
/// </param>
/// <param name="AverageQuality">
/// Mean of <see cref="SleepEntryDto.Quality"/> (scale 1–5) over <paramref name="Entries"/>, rounded to 2 decimal places;
/// <see langword="null"/> when there are no entries.
/// </param>
public sealed record SleepDiaryDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SleepEntryDto> Entries,
    double? AverageSleepMinutes,
    double? AverageQuality);
