using System.Net;
using System.Text.RegularExpressions;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>
/// The part of a browser that the login of <c>bff-web</c> needs: a cookie jar, redirects and HTML forms. Used by <c>dotnet superapp e2e</c>
/// to log in through the local CIAM like the SPA would, without a real browser.
/// </summary>
/// <remarks>
/// <para>
/// Cookies are kept by name for <c>localhost</c> regardless of the port, which is enough for the local environment (bff-web on 5001, the
/// realm on 8081). The raw <c>Set-Cookie</c> of every cookie is kept as well, so a check can read its attributes (<c>Secure</c>,
/// <c>HttpOnly</c>, <c>SameSite</c>). A cookie set with an empty value or <c>Max-Age=0</c> is removed, as a browser does at logout.
/// </para>
/// <para>
/// Redirects are not followed automatically: <see cref="FollowAsync"/> follows them on request, so a check can inspect a redirect first.
/// Forms are read with <see cref="Form"/>, which is written for the login page of the realm and the <c>form_post</c> response of the
/// authorization endpoint, not for arbitrary HTML. The certificate check is skipped for <c>localhost</c> only (see <see cref="LocalHttp"/>).
/// </para>
/// </remarks>
internal sealed partial class LocalBrowser : IDisposable
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly HttpClient _client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.Host == "localhost",
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>Raw <c>Set-Cookie</c> header of the last time each cookie was set, by cookie name.</summary>
    public IDictionary<string, string> SetCookieHeaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Sends a GET request with the cookies of the jar.</summary>
    /// <param name="url">Absolute URL.</param>
    /// <param name="headers">Extra request headers, e.g. <c>X-CSRF</c>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response; status 0 when the component cannot be reached.</returns>
    public Task<BrowserResponse> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), headers, cancellationToken);

    /// <summary>Submits a form (<c>application/x-www-form-urlencoded</c>) with the cookies of the jar.</summary>
    /// <param name="url">Absolute action of the form.</param>
    /// <param name="fields">Form fields.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response; status 0 when the component cannot be reached.</returns>
    public Task<BrowserResponse> PostFormAsync(Uri url, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) }, null, cancellationToken);

    /// <summary>Follows redirects with GET, as a browser does after a navigation, at most ten times.</summary>
    /// <param name="response">A response that may be a redirect.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The first response that is not a redirect.</returns>
    public async Task<BrowserResponse> FollowAsync(BrowserResponse response, CancellationToken cancellationToken = default)
    {
        for (var hops = 0; hops < 10 && response.IsRedirect; hops++)
        {
            response = await GetAsync(response.Location!, cancellationToken: cancellationToken);
        }

        return response;
    }

    /// <summary>Reads a form of an HTML page: its action and the values of its inputs (HTML entities decoded).</summary>
    /// <param name="page">The page, its URL resolves a relative action.</param>
    /// <param name="id">Id of the form, e.g. <c>kc-form-login</c>, or <see langword="null"/> for the first form.</param>
    /// <returns>The absolute action and the fields, or <see langword="null"/> when the page has no such form.</returns>
    public static (Uri Action, Dictionary<string, string> Fields)? Form(BrowserResponse page, string? id = null)
    {
        var match = (id is null ? FirstForm() : FormWithId(id)).Match(page.Body);
        if (!match.Success)
        {
            return null;
        }

        var fields = InputField().Matches(page.Body).ToDictionary(input => input.Groups[1].Value, input => WebUtility.HtmlDecode(input.Groups[2].Value), StringComparer.Ordinal);
        return (new Uri(page.Url, WebUtility.HtmlDecode(match.Groups[1].Value)), fields);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private async Task<BrowserResponse> SendAsync(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var message = request;
        if (_cookies.Count > 0)
        {
            message.Headers.Add("Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            message.Headers.Add(name, value);
        }

        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    Store(setCookie);
                }
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var location = response.Headers.Location is { } target ? new Uri(message.RequestUri!, target) : null;
            return new BrowserResponse((int)response.StatusCode, message.RequestUri!, location, body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new BrowserResponse(0, message.RequestUri!, null, exception.Message);
        }
    }

    private void Store(string setCookie)
    {
        var separator = setCookie.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        var name = setCookie[..separator];
        var value = setCookie[(separator + 1)..].Split(';', 2)[0];
        SetCookieHeaders[name] = setCookie;
        var attributes = setCookie.ToLowerInvariant();
        if (value.Length == 0 || attributes.Contains("max-age=0", StringComparison.Ordinal) || attributes.Contains("expires=thu, 01 jan 1970", StringComparison.Ordinal))
        {
            _cookies.Remove(name);
        }
        else
        {
            _cookies[name] = value;
        }
    }

    [GeneratedRegex("""<form[^>]*action="([^"]+)"[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FirstForm();

    [GeneratedRegex("""<input[^>]*name="([^"]+)"[^>]*value="([^"]*)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex InputField();

    private static Regex FormWithId(string id) =>
        new($"""<form[^>]*id="{Regex.Escape(id)}"[^>]*action="([^"]+)"[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline);
}
