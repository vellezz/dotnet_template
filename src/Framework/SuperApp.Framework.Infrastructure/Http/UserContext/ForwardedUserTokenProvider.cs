using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace SuperApp.Framework.Infrastructure.Http.UserContext;

/// <summary>
/// Default <see cref="IDownstreamTokenProvider"/>: the bearer token of the current HTTP request, passed on unchanged (ADR-0040).
/// </summary>
/// <remarks>
/// Reads the <c>Authorization</c> header of the incoming request through <see cref="IHttpContextAccessor"/>. The request has already passed
/// JWT validation of the host (<c>AddAppApi</c>), so only a validated token is passed on. A header with another scheme, or no header, gives
/// <see langword="null"/>.
/// </remarks>
/// <param name="httpContextAccessor">Access to the current request.</param>
internal sealed class ForwardedUserTokenProvider(IHttpContextAccessor httpContextAccessor) : IDownstreamTokenProvider
{
    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        return ValueTask.FromResult(
            AuthenticationHeaderValue.TryParse(header, out var value)
            && string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(value.Parameter)
                ? value.Parameter
                : null);
    }
}
