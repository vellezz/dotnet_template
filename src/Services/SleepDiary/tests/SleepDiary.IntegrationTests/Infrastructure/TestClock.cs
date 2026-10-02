using SuperApp.Framework.Application.Time;

namespace SleepDiary.IntegrationTests.Infrastructure;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
