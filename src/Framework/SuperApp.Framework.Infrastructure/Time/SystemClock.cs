using SuperApp.Framework.Application.Time;

namespace SuperApp.Framework.Infrastructure.Time;

/// <summary><see cref="IClock"/> backed by <see cref="TimeProvider"/>, registered by <c>AddAppServiceDefaults</c>.</summary>
internal sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
