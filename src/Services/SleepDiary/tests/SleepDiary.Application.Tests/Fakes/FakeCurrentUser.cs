using SuperApp.Framework.Application.Security;

namespace SleepDiary.Application.Tests.Fakes;

internal sealed class FakeCurrentUser(string? subject, params string[] scopes) : ICurrentUser
{
    public bool IsAuthenticated => subject is not null;

    public string? Subject => subject;

    public bool HasScope(string scope) => scopes.Contains(scope);
}
