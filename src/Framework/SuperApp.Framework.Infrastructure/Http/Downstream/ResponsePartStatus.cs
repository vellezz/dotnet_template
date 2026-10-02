namespace SuperApp.Framework.Infrastructure.Http.Downstream;

/// <summary>Whether a part of a composed response could be fetched, and if not, why.</summary>
/// <remarks>
/// The module renders every part independently: a part that is not <see cref="Ok"/> shows its own message while the others are shown
/// normally (partial rendering, ADR-0038), instead of the whole screen failing because one service is down. Serialized as a string
/// (<c>"Ok"</c>, <c>"Forbidden"</c>, ...).
/// </remarks>
public enum ResponsePartStatus
{
    /// <summary>The data was fetched; <c>data</c> is set.</summary>
    Ok,

    /// <summary>The user may not see this part (the service answered 401 or 403, e.g. a missing scope); hide it.</summary>
    Forbidden,

    /// <summary>The service answered with another error or could not be reached; show a retry message.</summary>
    Unavailable,

    /// <summary>The service did not answer within the part's time limit; show a retry message.</summary>
    Timeout,
}
