namespace SuperApp.Framework.Application.Time;

/// <summary>
/// Source of the current time for application and domain logic.
/// </summary>
/// <remarks>
/// <para>
/// Never call <see cref="DateTimeOffset.UtcNow"/> or <see cref="DateTime.Now"/> in handlers: inject <see cref="IClock"/> and pass
/// the time into aggregate methods as a parameter (for example <c>material.Publish(clock.UtcNow)</c>). The domain stays deterministic,
/// and tests control time with a fake clock.
/// </para>
/// <para>The framework registers an implementation based on <see cref="TimeProvider.System"/>.</para>
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant in UTC (offset zero). Store and compare times in UTC; convert to local time only for presentation.</summary>
    DateTimeOffset UtcNow { get; }
}
