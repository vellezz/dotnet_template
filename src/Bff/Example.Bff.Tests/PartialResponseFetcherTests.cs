using SuperApp.Framework.Infrastructure.Http.Downstream;
using System.Net;
using Example.Bff.Summary;
using Refit;

namespace Example.Bff.Tests;

/// <summary>Partial rendering: every failure of one service becomes a part status instead of failing the whole composed answer.</summary>
public sealed class PartialResponseFetcherTests
{
    [Fact]
    public async Task Successful_answer_is_mapped_to_an_ok_part()
    {
        var part = await FetchAsync(_ => Task.FromResult(Responses.Success(HttpStatusCode.OK, "data")));

        Assert.Equal(ResponsePartStatus.Ok, part.Status);
        Assert.Equal("DATA", part.Data);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ResponsePartStatus.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized, ResponsePartStatus.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, ResponsePartStatus.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, ResponsePartStatus.Unavailable)]
    public async Task Error_answer_is_mapped_to_its_part_status(HttpStatusCode status, ResponsePartStatus expected)
    {
        var part = await FetchAsync(_ => Responses.ErrorAsync<string>(status, """{"code":"x"}"""));

        Assert.Equal(expected, part.Status);
        Assert.Null(part.Data);
    }

    [Fact]
    public async Task Unreachable_service_is_unavailable_and_slow_service_times_out()
    {
        var unreachable = await FetchAsync(_ => throw new HttpRequestException("connection refused"));
        var slow = await FetchAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Responses.Success(HttpStatusCode.OK, "late");
        });

        Assert.Equal(ResponsePartStatus.Unavailable, unreachable.Status);
        Assert.Equal(ResponsePartStatus.Timeout, slow.Status);
    }

    [Fact]
    public async Task Own_time_limit_is_respected()
    {
        var started = DateTime.UtcNow;
        var part = await PartialResponseFetcher.FetchAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Responses.Success(HttpStatusCode.OK, "late");
            },
            value => value,
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);

        Assert.Equal(ResponsePartStatus.Timeout, part.Status);
        Assert.True(DateTime.UtcNow - started < PartialResponseFetcher.DefaultTimeLimit);
    }

    [Fact]
    public async Task Cancellation_by_the_client_is_not_hidden()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PartialResponseFetcher.FetchAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Responses.Success(HttpStatusCode.OK, "never");
            },
            value => value,
            cancelled.Token));
    }

    private static Task<ResponsePart<string>> FetchAsync(Func<CancellationToken, Task<IApiResponse<string>>> call) =>
        PartialResponseFetcher.FetchAsync(call, value => value.ToUpperInvariant(), TestContext.Current.CancellationToken);
}
