namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of <c>gateway.Sessions</c>: one server-side BFF session (ADR-0013). Written and read only by <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/>;
/// expired rows are deleted by <see cref="SuperApp.Gateway.Bff.Sessions.SessionCleanupService"/>.
/// </summary>
/// <remarks>
/// The authentication ticket (claims plus access, refresh and id tokens) is stored only encrypted in <see cref="Value"/>. The
/// <see cref="Subject"/> and <see cref="SessionId"/> columns are plain copies of two claims, indexed so that a back-channel logout can find
/// the sessions to revoke without decrypting tickets. Not a temporal table: sessions change on every token refresh.
/// </remarks>
internal sealed class Session
{
    /// <summary>Session key: 64 hexadecimal characters (32 random bytes); the only value stored in the browser cookie.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The serialized authentication ticket, encrypted with Data Protection.</summary>
    public byte[] Value { get; set; } = [];

    /// <summary>
    /// When the session expires (from the ticket's <c>ExpiresUtc</c>, moved forward by sliding expiration); <see langword="null"/> when the
    /// ticket has no expiry.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>User id from the <c>sub</c> claim; used to revoke all sessions of a user. Max. 200 characters.</summary>
    public string? Subject { get; set; }

    /// <summary>CIAM SSO session id from the <c>sid</c> claim; used by back-channel logout. Max. 200 characters.</summary>
    public string? SessionId { get; set; }

    /// <summary>When the row was last written (login, sliding renewal or token refresh).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
