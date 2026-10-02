namespace Example.Bff.Summary;

/// <summary>The user's sleep in the last seven days (today and the six days before, by entry date).</summary>
/// <param name="From">First day of the period, <c>YYYY-MM-DD</c>.</param>
/// <param name="To">Last day of the period (today in UTC), <c>YYYY-MM-DD</c>.</param>
/// <param name="EntryCount">Number of days with a diary entry in the period (0–7).</param>
/// <param name="AverageSleepMinutes">Average sleep time of those days in minutes, or <see langword="null"/> without entries.</param>
/// <param name="AverageQuality">Average quality (1–5) of those days, or <see langword="null"/> without entries.</param>
public sealed record SleepWeekDto(DateOnly From, DateOnly To, int EntryCount, double? AverageSleepMinutes, double? AverageQuality);
