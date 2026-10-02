using SuperApp.Framework.Infrastructure.Http.UserContext;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Example.Bff.Tests;

/// <summary>Calls in the user's context carry the incoming bearer token unchanged; nothing else is ever sent as identity (ADR-0040).</summary>
public sealed class UserTokenForwardingTests
{
    [Fact]
    public async Task Incoming_bearer_token_is_sent_unchanged_and_replaces_any_header_set_by_the_caller()
    {
        var sent = await SendAsync(incomingAuthorization: "Bearer user-jwt", outgoingAuthorization: "Bearer something-else");

        Assert.Equal("Bearer user-jwt", sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public async Task Without_a_bearer_token_no_authorization_header_is_sent(string? incomingAuthorization)
    {
        var sent = await SendAsync(incomingAuthorization, outgoingAuthorization: "Bearer something-else");

        Assert.Null(sent);
    }

    private static async Task<string?> SendAsync(string? incomingAuthorization, string? outgoingAuthorization)
    {
        var recorder = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("downstream").AddUserTokenForwarding().ConfigurePrimaryHttpMessageHandler(() => recorder);
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        if (incomingAuthorization is not null)
        {
            context.Request.Headers.Authorization = incomingAuthorization;
        }

        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("downstream");
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://service/v1/x");
        if (outgoingAuthorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", outgoingAuthorization);
        }

        using var _ = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return recorder.Authorization;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
