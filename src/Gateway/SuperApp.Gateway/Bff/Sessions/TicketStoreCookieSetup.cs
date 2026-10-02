using Microsoft.AspNetCore.Authentication.Cookies;

namespace SuperApp.Gateway.Bff.Sessions;

/// <summary>
/// Plugs <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/> into the cookie authentication options as <see cref="CookieAuthenticationOptions.SessionStore"/>,
/// which switches the cookie from "whole encrypted ticket" to "session key only" (ADR-0013).
/// </summary>
/// <remarks>
/// Implemented as post-configuration because <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/> is resolved from DI, which is not available inside the
/// <c>AddCookie</c> lambda in <c>Program.cs</c>. Applies to every cookie scheme; the gateway has only one.
/// </remarks>
/// <param name="ticketStore">The singleton session store.</param>
internal sealed class TicketStoreCookieSetup(DbTicketStore ticketStore) : Microsoft.Extensions.Options.IPostConfigureOptions<CookieAuthenticationOptions>
{
    /// <summary>Sets <see cref="CookieAuthenticationOptions.SessionStore"/> to the database-backed store.</summary>
    /// <param name="name">Name of the options instance (the authentication scheme); ignored.</param>
    /// <param name="options">The cookie options being configured.</param>
    public void PostConfigure(string? name, CookieAuthenticationOptions options) => options.SessionStore = ticketStore;
}
