namespace SuperApp.Cli.LocalEnvironment;

/// <summary>One response seen by <see cref="LocalBrowser"/>.</summary>
/// <param name="Status">HTTP status code; 0 when the component cannot be reached.</param>
/// <param name="Url">Absolute URL of the request, used to resolve relative redirects and form actions.</param>
/// <param name="Location">Absolute target of a redirect, or <see langword="null"/>.</param>
/// <param name="Body">Response body as text, or the connection error.</param>
internal sealed record BrowserResponse(int Status, Uri Url, Uri? Location, string Body)
{
    /// <summary>Whether the response is a redirect the browser would follow.</summary>
    public bool IsRedirect => Status is 301 or 302 or 303 or 307 or 308 && Location is not null;

    /// <summary>Status and the redirect target or the start of the body, for the report of a check.</summary>
    public string Summary
    {
        get
        {
            var text = (Location?.ToString() ?? Body).ReplaceLineEndings(" ");
            return $"{Status} {(text.Length > 120 ? text[..120] + "…" : text)}";
        }
    }
}
