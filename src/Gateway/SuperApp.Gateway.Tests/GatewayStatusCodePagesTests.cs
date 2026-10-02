using System.Text.Json;
using SuperApp.Framework.Infrastructure.Api;
using SuperApp.Gateway.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace SuperApp.Gateway.Tests;

/// <summary>Empty errors of the gateway itself get a problem body; answers relayed from a BFF stay unchanged (ADR-0037, ADR-0044).</summary>
public sealed class GatewayStatusCodePagesTests
{
    [Fact]
    public async Task Own_401_gets_code_and_trace_id()
    {
        var httpContext = Context(StatusCodes.Status401Unauthorized);

        await GatewayStatusCodePages.WriteAsync(StatusCode(httpContext));

        using var body = await Body(httpContext);
        Assert.Equal("auth.invalid_token", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Answer_relayed_from_a_bff_is_not_touched()
    {
        var httpContext = Context(StatusCodes.Status404NotFound);
        httpContext.Features.Set<IReverseProxyFeature>(new ReverseProxyFeature());

        await GatewayStatusCodePages.WriteAsync(StatusCode(httpContext));

        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    [Fact]
    public async Task Failed_forward_gets_a_body()
    {
        var httpContext = Context(StatusCodes.Status502BadGateway);
        httpContext.Features.Set<IReverseProxyFeature>(new ReverseProxyFeature());
        httpContext.Features.Set<IForwarderErrorFeature>(new ForwardFailed());

        await GatewayStatusCodePages.WriteAsync(StatusCode(httpContext));

        using var body = await Body(httpContext);
        Assert.Equal("server.error", body.RootElement.GetProperty("code").GetString());
    }

    private static DefaultHttpContext Context(int status)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsConventions.Apply)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = "/api/example/v1/me/summary";
        httpContext.Request.Headers.Accept = "application/json";
        httpContext.Response.Body = new MemoryStream();
        httpContext.Response.StatusCode = status;
        return httpContext;
    }

    private static StatusCodeContext StatusCode(HttpContext httpContext) =>
        new(httpContext, new StatusCodePagesOptions(), _ => Task.CompletedTask);

    private static async Task<JsonDocument> Body(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class ForwardFailed : IForwarderErrorFeature
    {
        public ForwarderError Error => ForwarderError.Request;

        public Exception? Exception => null;
    }
}
