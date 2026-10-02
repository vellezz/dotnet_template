using SuperApp.Framework.Infrastructure.Http.Downstream;
namespace Example.Bff.Summary;

/// <summary>
/// The start screen of the Example module, composed by the BFF from two domain services; each part is loaded and reported independently.
/// </summary>
/// <param name="Favorites">The user's favorites, from the Knowledge service.</param>
/// <param name="SleepWeek">The user's sleep in the last seven days, from the SleepDiary service.</param>
public sealed record MySummaryDto(ResponsePart<FavoritesSummaryDto> Favorites, ResponsePart<SleepWeekDto> SleepWeek);
