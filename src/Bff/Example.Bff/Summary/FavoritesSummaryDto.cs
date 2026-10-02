namespace Example.Bff.Summary;

/// <summary>The user's favorites shown on the summary screen.</summary>
/// <param name="TotalCount">Number of all favorites of the user.</param>
/// <param name="Latest">Up to five most recently added favorites, newest first.</param>
public sealed record FavoritesSummaryDto(int TotalCount, IReadOnlyList<FavoriteItemDto> Latest);
