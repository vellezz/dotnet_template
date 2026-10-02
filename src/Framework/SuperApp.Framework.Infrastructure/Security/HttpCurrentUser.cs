using SuperApp.Framework.Application.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SuperApp.Framework.Infrastructure.Security;

/// <summary>
/// <see cref="ICurrentUser"/> of API processes: reads the subject (<c>sub</c>) and scopes (<c>scope</c> or <c>scp</c>, space-separated) from the
/// JWT validated by the service (ADR-0012).
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? Subject => Principal?.FindFirst("sub")?.Value;

    public bool HasScope(string scope) =>
        Principal?.Claims
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal) == true;
}
