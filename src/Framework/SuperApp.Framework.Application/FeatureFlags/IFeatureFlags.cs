namespace SuperApp.Framework.Application.FeatureFlags;

/// <summary>
/// Port for reading feature flags in handlers: tells whether a feature is switched on for the current caller (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// Infrastructure implements it with PostHog when analytics is configured, and with application configuration
/// (<c>FeatureFlags:{key}</c>, falling back to <see cref="FeatureFlag.DefaultValue"/>) otherwise, which is the case in local development and
/// tests. Application code depends only on this interface, never on the PostHog SDK.
/// </para>
/// <para>
/// The flag is evaluated for the current user (<c>ICurrentUser.Subject</c>, sent to PostHog only as a pseudonymous analytics identifier) so
/// that percentage rollouts are sticky per user and match what the web and mobile clients see for the same person. Without a user
/// (a Worker consumer, an anonymous endpoint) it is evaluated for the fixed identifier <c>system</c>; target such calls in PostHog with a
/// condition on that identifier or roll them out at 0 % / 100 %.
/// </para>
/// <para>
/// Evaluation never fails a request: if PostHog cannot be reached or does not answer in time, <see cref="FeatureFlag.DefaultValue"/> is used,
/// a warning is logged and the <c>superapp.feature_flags.fallbacks</c> counter grows. Values are evaluated at most once per flag per DI scope (one
/// HTTP request or one consumed message), so a handler sees a consistent value even if the flag changes meanwhile.
/// </para>
/// <para>
/// Use flags in command and query handlers to switch behaviour; keep business rules in the aggregate (pass the decision in as an argument when
/// the aggregate needs it). A flag is not an authorization rule: scopes and resource rules still apply (ADR-0012).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class RateMaterialHandler(IFeatureFlags flags, ...) : ICommandHandler&lt;RateMaterial&gt;
/// {
///     public async Task&lt;Result&gt; Handle(RateMaterial command, CancellationToken cancellationToken)
///     {
///         if (!await flags.IsEnabledAsync(KnowledgeFeatureFlags.MaterialRatings, cancellationToken))
///         {
///             return LibraryErrors.RatingsNotAvailable;
///         }
///         // ...
///     }
/// }
/// </code>
/// </example>
public interface IFeatureFlags
{
    /// <summary>Tells whether <paramref name="flag"/> is switched on for the current caller.</summary>
    /// <param name="flag">The flag, declared in the service's <c>{Service}FeatureFlags</c> class.</param>
    /// <param name="cancellationToken">Cancellation of the request or message; cancelling stops waiting for the provider.</param>
    /// <returns>
    /// The value from the provider (PostHog or configuration), or <see cref="FeatureFlag.DefaultValue"/> when the provider does not know the flag
    /// or cannot answer.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken);
}
