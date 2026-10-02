using SuperApp.Framework.Application.Time;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
}
