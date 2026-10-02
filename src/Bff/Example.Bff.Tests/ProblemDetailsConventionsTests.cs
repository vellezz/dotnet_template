using System.Diagnostics;
using SuperApp.Framework.Infrastructure.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Example.Bff.Tests;

/// <summary>Problems created by ASP.NET Core get the same <c>code</c> and <c>traceId</c> as the problems written by the framework.</summary>
public sealed class ProblemDetailsConventionsTests
{
    [Theory]
    [InlineData(401, "auth.invalid_token")]
    [InlineData(403, "auth.forbidden")]
    [InlineData(404, "http.not_found")]
    [InlineData(400, "request.malformed")]
    [InlineData(500, "server.error")]
    [InlineData(409, "http.409")]
    public void Problem_without_code_gets_the_code_of_its_status(int status, string code)
    {
        var context = Context(new ProblemDetails { Status = status });

        ProblemDetailsConventions.Apply(context);

        Assert.Equal(code, context.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Binding_error_gets_request_malformed_not_validation_failed()
    {
        var context = Context(new ValidationProblemDetails { Status = 400 });

        ProblemDetailsConventions.Apply(context);

        Assert.Equal("request.malformed", context.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Existing_code_is_kept()
    {
        var problem = new ProblemDetails { Status = 403 };
        problem.Extensions["code"] = "auth.missing_scope";
        var context = Context(problem);

        ProblemDetailsConventions.Apply(context);

        Assert.Equal("auth.missing_scope", context.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Trace_id_is_the_32_character_trace_id_of_the_activity()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var problem = new ProblemDetails { Status = 401 };
        problem.Extensions["traceId"] = "00-" + activity.TraceId + "-" + activity.SpanId + "-01";
        var context = Context(problem);

        ProblemDetailsConventions.Apply(context);

        Assert.Equal(activity.TraceId.ToString(), context.ProblemDetails.Extensions["traceId"]);
        Assert.Matches("^[0-9a-f]{32}$", (string)context.ProblemDetails.Extensions["traceId"]!);
        Assert.Equal("/v1/me/summary", context.ProblemDetails.Instance);
    }

    private static ProblemDetailsContext Context(ProblemDetails problem)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/v1/me/summary";
        return new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem };
    }
}
