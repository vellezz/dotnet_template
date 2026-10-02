using SuperApp.Framework.Application.Security;

namespace SuperApp.Framework.Infrastructure.Security;

/// <summary>
/// <see cref="ICurrentUser"/> of Worker processes: an authenticated system identity with every scope and no subject, used for commands sent by
/// message consumers.
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string? Subject => null;

    public bool HasScope(string scope) => true;
}
