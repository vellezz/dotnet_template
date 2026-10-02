namespace ExperienceName.Bff.Security;

/// <summary>Scopes checked by the BFF of the ExperienceName experience itself (ADR-0039, ADR-0040).</summary>
/// <remarks>
/// The public API needs no BFF scope: every call is passed on to a domain service, which checks its own scopes. Only the internal API, meant
/// for BFFs of other experiences, is protected here. Scopes follow <c>{experience}.internal.{action}</c>; system calls (without a user) would
/// use <c>{experience}.internal.system.{action}</c> and need an explicit justification in the contract.
/// </remarks>
public static class ExperienceNameBffScopes
{
    /// <summary>
    /// <c>experiencename.internal.read</c>: read the internal API of the ExperienceName experience in the user's context. Granted to clients
    /// of other experiences that show its data.
    /// </summary>
    public const string InternalRead = "experiencename.internal.read";
}
