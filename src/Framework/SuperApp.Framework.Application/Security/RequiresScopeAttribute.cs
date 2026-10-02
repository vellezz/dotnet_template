namespace SuperApp.Framework.Application.Security;

/// <summary>
/// Declares the OAuth scope the caller's access token must contain to execute a command or query.
/// The authorization pipeline behavior checks it before validation and before the handler runs.
/// </summary>
/// <remarks>
/// <para>Authorization in this system has two levels (ADR-0012):</para>
/// <list type="bullet">
///   <item><description><b>Coarse-grained</b>, declarative: "may this client perform this kind of operation at all?" expressed as a scope in the
///   <c>{service}.{resource}.{action}</c> convention (<c>knowledge.catalog.write</c>). The gateway checks it per route and the service checks it
///   again here, per command or query, because services trust only their own token validation.</description></item>
///   <item><description><b>Fine-grained</b>, resource-level: "may this user change this particular entry?" implemented in the handler or the aggregate
///   using <see cref="ICurrentUser"/>, returning an <see cref="SuperApp.Framework.Domain.Results.ErrorType.Forbidden"/> or <see cref="SuperApp.Framework.Domain.Results.ErrorType.NotFound"/> error.</description></item>
/// </list>
/// <para>
/// Put the attribute on every command and query record. Define scope names as constants in a <c>{Service}Scopes</c> class of the Application
/// project and never repeat them as string literals. A request with neither this attribute nor <see cref="AllowAnonymousRequestAttribute"/>
/// only requires an authenticated caller. When the scope is missing, the request ends with <c>auth.missing_scope</c> (HTTP 403).
/// </para>
/// <para>In the Worker, the system identity has every scope, so the attribute never blocks commands sent by consumers.</para>
/// </remarks>
/// <example>
/// <code>
/// public static class KnowledgeScopes
/// {
///     public const string CatalogRead = "knowledge.catalog.read";
///     public const string CatalogWrite = "knowledge.catalog.write";
/// }
///
/// [RequiresScope(KnowledgeScopes.CatalogWrite)]
/// public sealed record CreateCategory(string Name, string Slug) : ICommand&lt;Result&lt;Guid&gt;&gt;;
/// </code>
/// </example>
/// <param name="scope">Scope the token must contain, e.g. <c>knowledge.catalog.write</c>; compared exactly (case-sensitive).</param>
/// <seealso cref="AllowAnonymousRequestAttribute"/>
/// <seealso cref="ICurrentUser"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresScopeAttribute(string scope) : Attribute
{
    /// <summary>Gets the scope the caller's token must contain; without it the request fails with <c>auth.missing_scope</c>.</summary>
    public string Scope { get; } = scope;
}
