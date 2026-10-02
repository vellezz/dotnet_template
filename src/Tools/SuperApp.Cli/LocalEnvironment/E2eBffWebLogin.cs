using System.Text.Json;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>
/// The browser part of <c>dotnet superapp e2e</c>: a full login of the local user <c>reader</c> to <c>bff-web</c> through the local CIAM,
/// requests of the SPA with the session cookie and the logout (ADR-0011, ADR-0013).
/// </summary>
/// <remarks>
/// <para>Checks, in order:</para>
/// <list type="number">
///   <item><description><c>/bff/login</c> redirects to the authorization endpoint of the realm; after the login form and the <c>form_post</c>
///   callback the gateway redirects to <c>/</c> and sets <c>__Host-bff</c> with <c>Secure</c>, <c>HttpOnly</c> and <c>SameSite=Strict</c>.</description></item>
///   <item><description><c>/bff/user</c> answers 200 with the granted scopes and a <c>logoutUrl</c>.</description></item>
///   <item><description>an API request without <c>X-CSRF: 1</c> answers <c>401 auth.csrf_header_missing</c>; with the header the gateway
///   forwards it with the token of the session (200 for the Knowledge categories of the experience, else 404 <c>http.not_found</c> of the BFF).</description></item>
///   <item><description>a logout with a foreign <c>sid</c> answers 400 and keeps the session; the real logout redirects to the end-session
///   endpoint of the realm, and afterwards <c>/bff/user</c> answers 401.</description></item>
/// </list>
/// <para>The session ends with the logout, so the scenario leaves no session behind.</para>
/// </remarks>
internal static class E2eBffWebLogin
{
    private const string SessionCookie = "__Host-bff";

    /// <summary>Runs the checks.</summary>
    /// <param name="bffWeb">Address of bff-web, e.g. <c>https://localhost:5001</c>.</param>
    /// <param name="apiPath">Path of an API request of the SPA, e.g. <c>/api/example/v1/knowledge/categories</c>.</param>
    /// <param name="apiStatus">Expected status of that request with the CSRF header.</param>
    /// <param name="check">Records one check: name, result, detail.</param>
    /// <param name="cancellationToken">Cancels the scenario.</param>
    /// <returns>A task completed when all checks ran or one failed in a way that makes the next ones meaningless.</returns>
    public static async Task RunAsync(string bffWeb, string apiPath, int apiStatus, Action<string, bool, string> check, CancellationToken cancellationToken)
    {
        using var browser = new LocalBrowser();
        var web = new Uri(bffWeb);

        var anonymous = await browser.GetAsync(new Uri(web, "/bff/user"), cancellationToken: cancellationToken);
        check("bff-web without a session: /bff/user 401", anonymous.Status == 401, anonymous.Summary);

        var start = await browser.GetAsync(new Uri(web, "/bff/login?returnUrl=/"), cancellationToken: cancellationToken);
        var toRealm = start.Location?.AbsolutePath == $"/realms/{LocalEndpoints.Realm}/protocol/openid-connect/auth";
        check("bff-web /bff/login redirects to the CIAM (Authorization Code + PKCE)", start.Status == 302 && toRealm, start.Summary);
        if (!toRealm)
        {
            return;
        }

        var page = await browser.FollowAsync(start, cancellationToken);
        if (LocalBrowser.Form(page, "kc-form-login") is not { } login)
        {
            check("CIAM shows the login form", false, page.Summary);
            return;
        }

        var posted = await browser.PostFormAsync(login.Action,
            new Dictionary<string, string> { ["username"] = "reader", ["password"] = "reader", ["credentialId"] = string.Empty }, cancellationToken);
        if (LocalBrowser.Form(posted) is not { } callback)
        {
            check("CIAM accepts the user reader and posts the code back (form_post)", false, posted.Summary);
            return;
        }

        var final = await browser.PostFormAsync(callback.Action, callback.Fields, cancellationToken);
        check("bff-web callback creates the session and redirects to /", final.Status == 302 && final.Location?.AbsolutePath == "/", final.Summary);
        var cookie = browser.SetCookieHeaders.TryGetValue(SessionCookie, out var raw) ? raw.ToLowerInvariant() : string.Empty;
        check($"{SessionCookie} cookie is Secure, HttpOnly, SameSite=Strict",
            cookie.Contains("secure", StringComparison.Ordinal) && cookie.Contains("httponly", StringComparison.Ordinal) && cookie.Contains("samesite=strict", StringComparison.Ordinal),
            cookie.Length == 0 ? "not set" : "set");

        var user = await browser.GetAsync(new Uri(web, "/bff/user"), cancellationToken: cancellationToken);
        var logoutUrl = Property(user.Body, "logoutUrl");
        check("bff-web /bff/user: 200 with scopes and logoutUrl", user.Status == 200 && Property(user.Body, "scopes") is not null && logoutUrl is not null, user.Summary);

        var withoutCsrf = await browser.GetAsync(new Uri(web, apiPath), cancellationToken: cancellationToken);
        check("bff-web API without X-CSRF: 401 auth.csrf_header_missing",
            withoutCsrf.Status == 401 && Property(withoutCsrf.Body, "code") == "auth.csrf_header_missing", withoutCsrf.Summary);
        var withCsrf = await browser.GetAsync(new Uri(web, apiPath), new Dictionary<string, string> { ["X-CSRF"] = "1" }, cancellationToken);
        check($"bff-web API with X-CSRF and the session: {apiStatus} from {apiPath}", withCsrf.Status == apiStatus, withCsrf.Summary);

        if (logoutUrl is null)
        {
            return;
        }

        var foreign = await browser.GetAsync(new Uri(web, "/bff/logout?sid=e2e-foreign"), cancellationToken: cancellationToken);
        var stillIn = await browser.GetAsync(new Uri(web, "/bff/user"), cancellationToken: cancellationToken);
        check("bff-web logout with a foreign sid: 400, the session stays", foreign.Status == 400 && stillIn.Status == 200, foreign.Summary);

        var logout = await browser.GetAsync(new Uri(web, logoutUrl), cancellationToken: cancellationToken);
        check("bff-web logout redirects to the end-session endpoint of the CIAM",
            logout.Status == 302 && logout.Location?.AbsolutePath.EndsWith("/protocol/openid-connect/logout", StringComparison.Ordinal) == true, logout.Summary);
        var realmPage = await browser.FollowAsync(logout, cancellationToken);
        if (LocalBrowser.Form(realmPage) is { } confirm)
        {
            await browser.FollowAsync(await browser.PostFormAsync(confirm.Action, confirm.Fields, cancellationToken), cancellationToken);
        }

        var after = await browser.GetAsync(new Uri(web, "/bff/user"), cancellationToken: cancellationToken);
        check("bff-web after logout: /bff/user 401", after.Status == 401, after.Summary);
    }

    private static string? Property(string body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
