namespace SuperApp.Framework.Infrastructure.Http.Downstream;

/// <summary>One independently fetched part of a response that a BFF composes from several services, with its status.</summary>
/// <remarks>
/// Created by <see cref="PartialResponseFetcher"/> and returned to the module as part of a composed DTO (for example
/// <c>MySummaryDto(ResponsePart&lt;FavoritesSummaryDto&gt; Favorites, ...)</c>). In the OpenAPI contract it appears as
/// <c>ResponsePartOf{T}</c> with <c>status</c> and <c>data</c>; the module shows <c>data</c> when <c>status</c> is <c>Ok</c> and a message
/// for the other statuses (partial rendering, ADR-0038).
/// </remarks>
/// <typeparam name="T">The data of the part, a DTO of the BFF.</typeparam>
/// <param name="Status">Whether the data could be fetched; see <see cref="ResponsePartStatus"/>.</param>
/// <param name="Data">The data when <paramref name="Status"/> is <see cref="ResponsePartStatus.Ok"/>; otherwise <see langword="null"/>.</param>
public sealed record ResponsePart<T>(ResponsePartStatus Status, T? Data)
    where T : class;
