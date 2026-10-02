using SuperApp.Framework.Application.Time;

namespace SleepDiary.Application.Tests.Fakes;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
