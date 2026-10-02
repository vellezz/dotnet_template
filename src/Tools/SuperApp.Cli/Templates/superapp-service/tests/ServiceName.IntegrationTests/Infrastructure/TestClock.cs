using SuperApp.Framework.Application.Time;

namespace ServiceName.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="IClock"/> returning the real current time, for integration tests that run the full pipeline against the database.
/// </summary>
/// <remarks>Use the Application tests with <c>FakeClock</c> when a test depends on exact timestamps.</remarks>
public sealed class TestClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
