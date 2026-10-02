namespace SuperApp.Framework.Domain.Results;

/// <summary>
/// Lets generic code create a failed result of any result type without reflection. Implemented by <see cref="Result"/> and <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline behaviors (authorization, validation) run before the handler and must be able to stop the request with an error,
/// whatever the response type of the request is. The generic constraint <c>where TResponse : IResultFactory&lt;TResponse&gt;</c>
/// on commands, queries and behaviors allows them to call <c>TResponse.FromError(error)</c>.
/// </para>
/// <para>
/// This constraint is also why every command and query must return <see cref="Result"/> or <see cref="Result{T}"/>:
/// a request returning a plain DTO would not compile. You never need to implement this interface yourself.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing result type itself (curiously recurring generic pattern).</typeparam>
public interface IResultFactory<TSelf>
    where TSelf : IResultFactory<TSelf>
{
    /// <summary>Creates a failed result of type <typeparamref name="TSelf"/>.</summary>
    /// <param name="error">The reason of the failure.</param>
    /// <returns>A failed result carrying <paramref name="error"/>.</returns>
    static abstract TSelf FromError(Error error);
}
