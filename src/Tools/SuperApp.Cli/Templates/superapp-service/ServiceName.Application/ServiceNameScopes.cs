namespace ServiceName.Application;

/// <summary>
/// OAuth scopes of the ServiceName service: they decide which commands and queries a caller may send.
/// </summary>
/// <remarks>
/// <para>
/// Scopes follow the convention <c>{service}.{resource}.{action}</c> (ADR-0012), e.g. <c>servicename.orders.read</c>, and are issued by
/// the CIAM in the access token (claim <c>scope</c>). TODO when creating the service: add one constant per resource and action,
/// document what each one allows, and hand the list over to the CIAM team (ADR-0016) and to the gateway route policies.
/// </para>
/// <para>
/// Every command and query declares its scope with <c>[RequiresScope(...)]</c>. The authorization pipeline behavior (ADR-0017) checks it
/// before validation and returns HTTP 403 when the token does not contain it. The gateway checks scopes per route as well, but the
/// service never relies on that.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// /// &lt;summary&gt;Read things (&lt;c&gt;servicename.things.read&lt;/c&gt;).&lt;/summary&gt;
/// public const string ThingsRead = Prefix + "things.read";
///
/// [RequiresScope(ServiceNameScopes.ThingsRead)]
/// public sealed record GetThing(Guid ThingId) : IQuery&lt;ThingDto&gt;;
/// </code>
/// </example>
public static class ServiceNameScopes
{
    /// <summary>
    /// Common prefix of all scopes of the service (<c>servicename.</c>); define resource scopes as <c>Prefix + "{resource}.{action}"</c>.
    /// </summary>
    public const string Prefix = "servicename.";
}
