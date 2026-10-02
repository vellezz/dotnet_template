namespace SuperApp.Framework.Application.FeatureFlags;

/// <summary>
/// A feature flag known to the code: its key in the flag provider and the value the code uses when the provider cannot answer
/// (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// Declare each flag once, as a <c>static readonly</c> field of the service's <c>{Service}FeatureFlags</c> class in Application, next to
/// <c>{Service}Scopes</c>; never pass key literals around. The key is the flag key in PostHog, in snake case with the service prefix
/// (<c>knowledge_material_ratings</c>), so that flags of different services cannot collide in the shared PostHog project.
/// </para>
/// <para>
/// <see cref="DefaultValue"/> is what users get when PostHog is unreachable, slow, or does not know the key, and what every environment
/// without analytics (local development, tests) gets unless configuration overrides it. Choose the value that is safe to run with
/// indefinitely: usually <see langword="false"/> for a new feature being rolled out, <see langword="true"/> for a kill switch that guards
/// an existing feature.
/// </para>
/// <para>
/// A flag only switches behaviour; it is not a permission. Access is decided by scopes and resource rules (ADR-0012); a flag that hides a
/// feature in the UI must still be checked in the backend when the backend behaves differently.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static class KnowledgeFeatureFlags
/// {
///     /// &lt;summary&gt;Readers can rate published materials (rolled out gradually).&lt;/summary&gt;
///     public static readonly FeatureFlag MaterialRatings = new("knowledge_material_ratings", DefaultValue: false);
/// }
/// </code>
/// </example>
/// <param name="Key">
/// Flag key in the provider: lowercase letters, digits and underscores, starting with the service prefix (for example
/// <c>knowledge_material_ratings</c>). The key is also the configuration key used when analytics is disabled (<c>FeatureFlags:{Key}</c>).
/// </param>
/// <param name="DefaultValue">The value used when the provider cannot answer or does not know the flag; see the remarks.</param>
public sealed record FeatureFlag(string Key, bool DefaultValue);
