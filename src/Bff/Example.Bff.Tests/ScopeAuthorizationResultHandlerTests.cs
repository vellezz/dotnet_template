using System.Text.Json;
using SuperApp.Framework.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Example.Bff.Tests;

/// <summary>A request rejected by the scope policy of the internal API gets the same problem body as from a domain service.</summary>
public sealed class ScopeAuthorizationResultHandlerTests
{
    [Fact]
    public async Task Forbidden_result_is_answered_with_403_auth_missing_scope()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/internal/v1/widgets/sleep-summary";
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await new ScopeAuthorizationResultHandler().HandleAsync(_ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(403, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("auth.missing_scope", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Successful_result_continues_the_pipeline()
    {
        var called = false;
        var context = new DefaultHttpContext();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await new ScopeAuthorizationResultHandler().HandleAsync(_ => { called = true; return Task.CompletedTask; }, context, policy, PolicyAuthorizationResult.Success());

        Assert.True(called);
    }
}
