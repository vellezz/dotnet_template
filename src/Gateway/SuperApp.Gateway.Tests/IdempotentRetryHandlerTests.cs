using SuperApp.Gateway.Proxy.Resilience;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuperApp.Gateway.Tests;

public sealed class IdempotentRetryHandlerTests
{
    [Fact]
    public async Task Get_is_retried_after_transient_status()
    {
        var backend = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        using var response = await SendAsync(backend, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task Get_is_retried_after_connection_error()
    {
        var backend = new ScriptedHandler(null, HttpStatusCode.OK);

        using var response = await SendAsync(backend, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task Post_is_never_retried()
    {
        var backend = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        using var response = await SendAsync(backend, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task Retries_stop_after_limit()
    {
        var backend = new ScriptedHandler(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        using var response = await SendAsync(backend, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, backend.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var backend = new ScriptedHandler(HttpStatusCode.NotFound, HttpStatusCode.OK);

        using var response = await SendAsync(backend, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, backend.Calls);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMessageHandler backend, HttpMethod method)
    {
        var handler = new IdempotentRetryHandler(2, TimeSpan.Zero, NullLogger.Instance) { InnerHandler = backend };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(method, "http://knowledge-api.knowledge.svc.cluster.local:8080/v1/materials");
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Returns the scripted statuses in order (repeating the last one); <see langword="null"/> simulates a connection error.</summary>
    private sealed class ScriptedHandler(params HttpStatusCode?[] script) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = script[Math.Min(Calls++, script.Length - 1)];
            return status is { } code
                ? Task.FromResult(new HttpResponseMessage(code))
                : throw new HttpRequestException("Connection refused");
        }
    }
}
