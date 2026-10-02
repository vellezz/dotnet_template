using System.Security.Claims;
using SuperApp.Framework.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Example.Bff.Tests;

/// <summary>Every call of the internal API leaves an audit entry naming the calling client; public API calls do not.</summary>
public sealed class InternalApiCallAuditTests
{
    [Fact]
    public void Internal_call_is_logged_with_the_calling_client()
    {
        var logger = new RecordingLogger();

        new InternalApiCallAudit(logger).OnActionExecuting(Executing("/internal/v1/widgets/sleep-summary", "dashboard-bff"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(230, entry.Id.Id);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("dashboard-bff", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_call_is_not_logged()
    {
        var logger = new RecordingLogger();

        new InternalApiCallAudit(logger).OnActionExecuting(Executing("/v1/me/summary", "bff-web"));

        Assert.Empty(logger.Entries);
    }

    private static ActionExecutingContext Executing(string path, string azp)
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", azp)], "test")) };
        httpContext.Request.Path = path;
        var action = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(action, [], new Dictionary<string, object?>(), controller: new object());
    }

    private sealed class RecordingLogger : ILogger<InternalApiCallAudit>
    {
        public List<(EventId Id, LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, logLevel, formatter(state, exception)));
    }
}
