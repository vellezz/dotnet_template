using System.Text.Json;
using SuperApp.Framework.Infrastructure.Api;
using SuperApp.Gateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.Gateway.Tests;

/// <summary>A request rejected by the rate limiter gets the same problem body as every other error (ADR-0044).</summary>
public sealed class GatewayRateLimitsTests
{
    [Fact]
    public async Task Rejected_request_gets_429_with_code_and_trace_id()
    {
        var options = new RateLimiterOptions();
        GatewayRateLimits.Register(options, new ConfigurationBuilder().Build());
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(problem => problem.CustomizeProblemDetails = ProblemDetailsConventions.Apply)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = "/api/example/v1/me/summary";
        httpContext.Request.Headers.Accept = "application/json";
        httpContext.Response.Body = new MemoryStream();
        httpContext.Response.StatusCode = options.RejectionStatusCode;

        await options.OnRejected!(new OnRejectedContext { HttpContext = httpContext, Lease = null! }, TestContext.Current.CancellationToken);

        Assert.Equal(429, httpContext.Response.StatusCode);
        httpContext.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("http.too_many_requests", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }
}
