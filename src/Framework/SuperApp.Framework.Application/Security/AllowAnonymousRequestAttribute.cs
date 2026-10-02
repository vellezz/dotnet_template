namespace SuperApp.Framework.Application.Security;

/// <summary>
/// Marks a command or query that may be executed without an authenticated caller. The authorization pipeline behavior
/// then skips every check, including <see cref="RequiresScopeAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// By default every request requires an authenticated caller, both at the HTTP level (fallback authorization policy of the API)
/// and in the pipeline. Use this attribute only for data that is intentionally public, and remember to also allow anonymous access
/// on the controller action (<c>[AllowAnonymous]</c>) and on the gateway route; all three levels must agree.
/// </para>
/// <para>Handlers of anonymous requests must not assume <see cref="ICurrentUser.Subject"/> is set.</para>
/// </remarks>
/// <seealso cref="RequiresScopeAttribute"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnonymousRequestAttribute : Attribute;
