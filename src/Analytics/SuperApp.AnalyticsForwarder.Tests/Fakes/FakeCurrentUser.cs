using SuperApp.Framework.Application.Security;

namespace SuperApp.AnalyticsForwarder.Tests.Fakes;

internal sealed class FakeCurrentUser(string? subject) : ICurrentUser
{
    public bool IsAuthenticated => subject is not null;

    public string? Subject => subject;

    public bool HasScope(string scope) => true;
}
