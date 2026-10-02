using SuperApp.Gateway.Bff;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SuperApp.Gateway.Tests;

/// <summary>BFF logout as a top-level navigation <c>GET /bff/logout?sid=...</c>: the sid parameter is the CSRF protection (ADR-0011).</summary>
public sealed class BffLogoutTests
{
    [Fact]
    public void Matching_sid_signs_out_of_cookie_and_oidc()
    {
        var result = BffEndpoints.Logout(Context(sid: "session-1"), "session-1");

        var signOut = Assert.IsType<SignOutHttpResult>(result);
        Assert.Equal(["Cookies", "OpenIdConnect"], signOut.AuthenticationSchemes);
        Assert.Equal("/", signOut.Properties!.RedirectUri);
    }

    [Theory]
    [InlineData("other-session")]
    [InlineData("")]
    [InlineData(null)]
    public void Missing_or_different_sid_is_rejected_without_sign_out(string? sid)
    {
        Assert.IsType<BadRequest>(BffEndpoints.Logout(Context(sid: "session-1"), sid));
    }

    [Fact]
    public void Session_without_sid_claim_cannot_be_logged_out_by_guessing()
    {
        Assert.IsType<BadRequest>(BffEndpoints.Logout(Context(sid: null), "anything"));
    }

    [Fact]
    public void Without_session_redirects_home()
    {
        var context = new DefaultHttpContext();

        Assert.Equal("/", Assert.IsType<RedirectHttpResult>(BffEndpoints.Logout(context, "session-1")).Url);
    }

    [Fact]
    public void Logout_url_carries_encoded_sid()
    {
        Assert.Equal("/bff/logout?sid=a%2Bb%20c", BffEndpoints.LogoutUrl(Context(sid: "a+b c").User));
        Assert.Null(BffEndpoints.LogoutUrl(Context(sid: null).User));
    }

    private static DefaultHttpContext Context(string? sid)
    {
        var claims = new List<Claim> { new("sub", "user-1") };
        if (sid is not null)
        {
            claims.Add(new Claim("sid", sid));
        }

        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")) };
    }
}
