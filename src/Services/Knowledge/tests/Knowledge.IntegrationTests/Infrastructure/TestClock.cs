using SuperApp.Framework.Application.Time;

namespace Knowledge.IntegrationTests.Infrastructure;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
