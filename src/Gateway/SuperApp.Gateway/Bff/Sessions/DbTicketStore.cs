using SuperApp.Gateway.Persistence.Entities;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SuperApp.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Gateway.Bff.Sessions;

/// <summary>
/// Server-side session store of the BFF (<see cref="ITicketStore"/>) backed by the <c>gateway.Sessions</c> table in MSSQL (ADR-0013).
/// The browser cookie contains only a random session key; the authentication ticket with the claims and the OIDC tokens stays on the server.
/// </summary>
/// <remarks>
/// <para>How it is used by the cookie handler (wired in by <see cref="SuperApp.Gateway.Bff.Sessions.TicketStoreCookieSetup"/>):</para>
/// <list type="bullet">
///   <item><description>After login: <see cref="StoreAsync"/> generates a 256-bit random key, saves the ticket and the key goes into the
///   <c>__Host-bff</c> cookie.</description></item>
///   <item><description>On every request: <see cref="RetrieveAsync"/> loads the ticket for the key from the cookie and puts the key into
///   <see cref="AuthenticationProperties.Items"/> under <see cref="SessionKeyItem"/>, so that <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/>
///   knows which session it refreshes.</description></item>
///   <item><description>When the ticket changes (sliding expiration): <see cref="RenewAsync"/> overwrites the row, but never with older
///   tokens than the stored ones (see below).</description></item>
///   <item><description>When the access token is about to expire: <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/> refreshes the tokens
///   inside <see cref="UpdateExclusiveAsync"/>.</description></item>
///   <item><description>On logout: <see cref="RemoveAsync"/>; on CIAM back-channel logout: <see cref="RemoveBySessionAsync"/>.</description></item>
/// </list>
/// <para>
/// The ticket contains access and refresh tokens, so it is serialized and encrypted with ASP.NET Core Data Protection (purpose
/// <c>SuperApp.Gateway.Bff.SessionTicket.v1</c>) before it is written; the keys are shared by all replicas through
/// <see cref="SuperApp.Gateway.Bff.DataProtection.GatewayDataProtection"/>. The <c>sub</c> and <c>sid</c> claims are copied into plain columns so sessions can be revoked
/// without decrypting them. Expired rows are deleted by <see cref="SuperApp.Gateway.Bff.Sessions.SessionCleanupService"/>.
/// </para>
/// <para>
/// <b>Concurrency across replicas.</b> Several replicas can serve requests of the same session at the same time. Every write of an existing
/// session (<see cref="RenewAsync"/>, <see cref="UpdateExclusiveAsync"/>) therefore runs in a short transaction that first takes the
/// exclusive SQL Server application lock of that session (<c>sp_getapplock</c>, transaction-owned, resource <c>gateway-session:</c> followed
/// by a hash of the key, waiting at most <see cref="LockTimeout"/>), the same mechanism as <see cref="SuperApp.Gateway.Bff.Sessions.SessionCleanupService"/>.
/// Under the lock the stored ticket is read again, so a token refresh made by another replica is seen, and <see cref="RenewAsync"/> keeps the
/// stored tokens when they expire later than those of the ticket being written. Without this, a request that loaded the ticket before a
/// refresh could write the old, already rotated refresh token back, and the next refresh would end the session.
/// </para>
/// <para>
/// Registered as a singleton; every operation opens its own DI scope with a fresh <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/>. The
/// <see cref="ITicketStore"/> methods take no cancellation token, so their database calls cannot be cancelled.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope per operation to resolve a <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/>.</param>
/// <param name="dataProtection">Data Protection provider used to encrypt the tickets.</param>
/// <param name="timeProvider">Clock used for the expiry check and <see cref="SuperApp.Gateway.Persistence.Entities.Session.UpdatedAt"/>.</param>
internal sealed class DbTicketStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider dataProtection, TimeProvider timeProvider) : ITicketStore
{
    /// <summary>
    /// Key of the <see cref="AuthenticationProperties.Items"/> entry in which <see cref="RetrieveAsync"/> passes the session key to the rest
    /// of the request (read by <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/>). Never stored: it is removed before a ticket is written.
    /// </summary>
    public const string SessionKeyItem = ".gateway.session_key";

    /// <summary>
    /// How long a write waits for the application lock of a session held by another request or replica (for example during a token
    /// refresh). Longer than the refresh timeout of <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/>, so a normal refresh always finishes first.
    /// </summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("SuperApp.Gateway.Bff.SessionTicket.v1");

    /// <summary>Creates a new session for <paramref name="ticket"/> under a new random key.</summary>
    /// <param name="ticket">The authentication ticket created by the OIDC login.</param>
    /// <returns>The session key (64 hexadecimal characters) that the cookie handler puts into the cookie.</returns>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket);
        return key;
    }

    /// <summary>
    /// Saves <paramref name="ticket"/> under <paramref name="key"/>, inserting the row if it does not exist: encrypts the ticket and updates
    /// the expiry, <c>sub</c>, <c>sid</c> and <see cref="SuperApp.Gateway.Persistence.Entities.Session.UpdatedAt"/>. Runs under the session's
    /// application lock; when the stored tokens expire later (<c>expires_at</c>) than those of <paramref name="ticket"/>, another request has
    /// refreshed them in the meantime, so the stored tokens are kept (and copied into <paramref name="ticket"/>).
    /// </summary>
    /// <param name="key">The session key from the cookie.</param>
    /// <param name="ticket">The current ticket; <see cref="AuthenticationProperties.ExpiresUtc"/> becomes the session expiry.</param>
    /// <returns>A task that completes when the row has been saved.</returns>
    /// <exception cref="TimeoutException">The session's application lock was not granted within <see cref="LockTimeout"/>.</exception>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await TryLockSessionAsync(db, key, CancellationToken.None))
        {
            throw new TimeoutException($"Nie uzyskano blokady sesji BFF w czasie {LockTimeout}.");
        }

        var session = await db.Sessions.FindAsync(key);
        if (session is null)
        {
            session = new Session { Id = key };
            db.Sessions.Add(session);
        }
        else if (Unprotect(session) is { } stored && ExpiresAt(stored) > ExpiresAt(ticket))
        {
            ticket.Properties.StoreTokens(stored.Properties.GetTokens());
        }

        Write(session, ticket);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Runs <paramref name="update"/> on the stored ticket of a session while holding the session's exclusive application lock, and saves
    /// the ticket it returns before the lock is released. No other request of any replica can write the same session in the meantime.
    /// Used by <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/> to refresh the tokens once per session across all replicas.
    /// </summary>
    /// <remarks>
    /// The lock is held for the whole duration of <paramref name="update"/> (which may call the CIAM), so the callback must honour
    /// <paramref name="cancellationToken"/> and must not call other methods of this store for the same session (they would wait for the lock).
    /// </remarks>
    /// <param name="key">The session key (see <see cref="SessionKeyItem"/>).</param>
    /// <param name="update">
    /// Receives the currently stored ticket (<see langword="null"/> when the session no longer exists, has expired or cannot be decrypted)
    /// and <paramref name="cancellationToken"/>; returns the ticket to save, or <see langword="null"/> to leave the row unchanged.
    /// </param>
    /// <param name="cancellationToken">Bounds waiting for the lock and the whole update; cancelling rolls the transaction back.</param>
    /// <returns>
    /// <see langword="true"/> when the lock was obtained and <paramref name="update"/> ran; <see langword="false"/> when the lock was not
    /// granted within <see cref="LockTimeout"/> (another request is still working on the session), in which case <paramref name="update"/>
    /// was not called.
    /// </returns>
    public async Task<bool> UpdateExclusiveAsync(
        string key,
        Func<AuthenticationTicket?, CancellationToken, Task<AuthenticationTicket?>> update,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await TryLockSessionAsync(db, key, cancellationToken))
        {
            return false;
        }

        var session = await db.Sessions.FirstOrDefaultAsync(row => row.Id == key, cancellationToken);
        var stored = session is null || session.ExpiresAt <= timeProvider.GetUtcNow() ? null : Unprotect(session);
        var updated = await update(stored, cancellationToken);
        if (updated is not null && session is not null)
        {
            Write(session, updated);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Loads and decrypts the ticket stored under <paramref name="key"/> and adds the key to its properties (<see cref="SessionKeyItem"/>).</summary>
    /// <param name="key">The session key from the cookie.</param>
    /// <returns>
    /// The ticket, or <see langword="null"/> (the request is treated as anonymous) when the session does not exist, was revoked, has expired,
    /// or cannot be decrypted (for example after the Data Protection keys were lost).
    /// </returns>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == key);
        if (session is null || session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return null;
        }

        var ticket = Unprotect(session);
        if (ticket is not null)
        {
            ticket.Properties.Items[SessionKeyItem] = key;
        }

        return ticket;
    }

    /// <summary>Deletes the session stored under <paramref name="key"/> (local logout); does nothing when it does not exist.</summary>
    /// <param name="key">The session key from the cookie.</param>
    /// <returns>A task that completes when the row has been deleted.</returns>
    public async Task RemoveAsync(string key)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await db.Sessions.Where(row => row.Id == key).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Revokes sessions after a back-channel logout from the CIAM. When <paramref name="sessionId"/> is given, only sessions of that SSO
    /// session are deleted; only when it is <see langword="null"/> are all sessions of <paramref name="subject"/> deleted.
    /// </summary>
    /// <param name="sessionId">The CIAM SSO session id (<c>sid</c> claim of the logout token), or <see langword="null"/> when the token has none.</param>
    /// <param name="subject">The user id (<c>sub</c> claim), used only when <paramref name="sessionId"/> is <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation of the back-channel request.</param>
    /// <returns>The number of deleted sessions; 0 when both arguments are <see langword="null"/> or nothing matched.</returns>
    public async Task<int> RemoveBySessionAsync(string? sessionId, string? subject, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        return await db.Sessions
            .Where(row => (sessionId != null && row.SessionId == sessionId) || (sessionId == null && subject != null && row.Subject == subject))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Takes the transaction-owned exclusive application lock of one session, waiting at most LockTimeout. The resource name contains a hash
    // of the key, so the session key itself never shows up in lock diagnostics (sys.dm_tran_locks). A negative result means "not granted".
    private static async Task<bool> TryLockSessionAsync(GatewayDbContext db, string key, CancellationToken cancellationToken)
    {
        var resource = "gateway-session:" + Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(key)), 0, 16);
        var lockResult = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout",
            [lockResult, new SqlParameter("@resource", resource), new SqlParameter("@timeout", (int)LockTimeout.TotalMilliseconds)],
            cancellationToken);
        return (int)lockResult.Value >= 0;
    }

    // Expiry of the access token stored in the ticket ("expires_at"); a ticket without it counts as the oldest.
    private static DateTimeOffset ExpiresAt(AuthenticationTicket ticket) =>
        DateTimeOffset.TryParse(ticket.Properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
            ? expiresAt
            : DateTimeOffset.MinValue;

    // Decrypts a stored ticket; null when it cannot be decrypted (for example after the Data Protection keys were lost).
    private AuthenticationTicket? Unprotect(Session session)
    {
        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Value));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Encrypts the ticket (without the request-only SessionKeyItem) into the row and updates the plain columns.
    private void Write(Session session, AuthenticationTicket ticket)
    {
        var copy = ticket.Clone();
        copy.Properties.Items.Remove(SessionKeyItem);
        session.Value = _protector.Protect(TicketSerializer.Default.Serialize(copy));
        session.ExpiresAt = ticket.Properties.ExpiresUtc;
        session.Subject = ticket.Principal.FindFirst("sub")?.Value;
        session.SessionId = ticket.Principal.FindFirst("sid")?.Value;
        session.UpdatedAt = timeProvider.GetUtcNow();
    }
}
