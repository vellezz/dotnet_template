using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Refit;

namespace SuperApp.Framework.Infrastructure.Http.Downstream;

/// <summary>
/// Fetches one part of a response that a BFF composes from several services (for example the favorites part of a summary screen): calls one
/// service with its own time limit and turns the outcome into a <see cref="ResponsePart{T}"/> with a status, never into an exception.
/// </summary>
/// <remarks>
/// <para>
/// Partial rendering (ADR-0038): the composing endpoint (not this class) starts all parts at once and awaits them together, so one slow or
/// failing service never breaks the whole answer. Each part is bounded by its time limit (<see cref="DefaultTimeLimit"/> unless given), so the
/// slowest service decides at most that long. The endpoint always answers 200, and the module renders each part by its status.
/// </para>
/// <para>Classification of the outcome:</para>
/// <list type="bullet">
///   <item><description>success → <see cref="ResponsePartStatus.Ok"/> with the mapped data;</description></item>
///   <item><description>answer 401 or 403 → <see cref="ResponsePartStatus.Forbidden"/> (the user may not see this part);</description></item>
///   <item><description>other error answer, connection failure, open circuit → <see cref="ResponsePartStatus.Unavailable"/>;</description></item>
///   <item><description>time limit or resilience timeout → <see cref="ResponsePartStatus.Timeout"/>.</description></item>
/// </list>
/// <para>
/// Cancellation of the incoming request by the client is not hidden: it propagates. The call itself is a client registered with
/// <c>AddDownstreamApi</c>, so the user's token, resilience and ISO formatting apply as for any other downstream call.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var favorites = PartialResponseFetcher.FetchAsync(
///     token =&gt; knowledge.LibraryFavoritesAsync(page: 1, pageSize: 5, token),
///     page =&gt; new FavoritesSummaryDto(page.TotalCount, ...),
///     cancellationToken);
/// var sleepWeek = PartialResponseFetcher.FetchAsync(token =&gt; sleepDiary.SleepEntriesListAsync(from, to, token), ToDto, cancellationToken);
///
/// return new MySummaryDto(await favorites, await sleepWeek);
/// </code>
/// </example>
public static class PartialResponseFetcher
{
    /// <summary>Default time limit of one part: 2 seconds.</summary>
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(2);

    /// <summary>Calls <paramref name="call"/> within <see cref="DefaultTimeLimit"/> and maps its answer to a response part.</summary>
    /// <typeparam name="TResponse">Body type of the service's answer.</typeparam>
    /// <typeparam name="TData">Data type of the part.</typeparam>
    /// <param name="call">The service call; receives a token cancelled after the time limit or when the request is cancelled.</param>
    /// <param name="map">Maps a successful body to the part's data.</param>
    /// <param name="cancellationToken">Cancellation of the incoming request.</param>
    /// <returns>The part with its status; never throws for a service failure.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<ResponsePart<TData>> FetchAsync<TResponse, TData>(
        Func<CancellationToken, Task<IApiResponse<TResponse>>> call,
        Func<TResponse, TData> map,
        CancellationToken cancellationToken)
        where TData : class =>
        FetchAsync(call, map, DefaultTimeLimit, cancellationToken);

    /// <summary>Calls <paramref name="call"/> within <paramref name="timeLimit"/> and maps its answer to a response part.</summary>
    /// <typeparam name="TResponse">Body type of the service's answer.</typeparam>
    /// <typeparam name="TData">Data type of the part.</typeparam>
    /// <param name="call">The service call; receives a token cancelled after <paramref name="timeLimit"/> or when the request is cancelled.</param>
    /// <param name="map">Maps a successful body to the part's data.</param>
    /// <param name="timeLimit">Maximum time the part may take; must be positive.</param>
    /// <param name="cancellationToken">Cancellation of the incoming request.</param>
    /// <returns>The part with its status; never throws for a service failure.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<ResponsePart<TData>> FetchAsync<TResponse, TData>(
        Func<CancellationToken, Task<IApiResponse<TResponse>>> call,
        Func<TResponse, TData> map,
        TimeSpan timeLimit,
        CancellationToken cancellationToken)
        where TData : class
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);

        try
        {
            var response = await call(limit.Token);
            if (response is { IsSuccessful: true, Content: { } content })
            {
                return new ResponsePart<TData>(ResponsePartStatus.Ok, map(content));
            }

            return new ResponsePart<TData>(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? ResponsePartStatus.Forbidden : ResponsePartStatus.Unavailable,
                null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                                          && exception is OperationCanceledException or TimeoutRejectedException)
        {
            return new ResponsePart<TData>(ResponsePartStatus.Timeout, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                                          && exception is HttpRequestException or BrokenCircuitException or ApiException)
        {
            return new ResponsePart<TData>(ResponsePartStatus.Unavailable, null);
        }
    }
}
