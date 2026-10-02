using SuperApp.Framework.Application.Time;

namespace ServiceName.Application.Tests.Fakes;

/// <summary>
/// Controllable <see cref="IClock"/> for command handler tests: time stands still until the test changes <see cref="UtcNow"/>.
/// </summary>
/// <param name="now">The initial time returned by <see cref="UtcNow"/>; use a fixed value so assertions on timestamps are exact.</param>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    /// <summary>Gets or sets the current time; set it to simulate time passing (e.g. expiry rules).</summary>
    public DateTimeOffset UtcNow { get; set; } = now;
}
