using SuperApp.Framework.Application.Security;

namespace SleepDiary.IntegrationTests.Infrastructure;

public sealed class TestCurrentUser : ICurrentUser
{
    public string? Subject { get; set; } = "test-user";

    public HashSet<string> Scopes { get; } = [];

    public bool IsAuthenticated => Subject is not null;

    public bool HasScope(string scope) => Scopes.Contains(scope);
}
