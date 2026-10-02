using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Knowledge.Application.Tests;

public sealed class PipelineRegistrationTests
{
    /// <summary>Every command of the service has a handler registered from the Application assembly (query handlers live in Infrastructure, ADR-0026, so only commands are checked here).</summary>
    [Fact]
    public void Every_command_has_a_registered_handler()
    {
        var services = new ServiceCollection();
        services.AddAppApplication(KnowledgeApplication.Assembly);

        var commands = KnowledgeApplication.Assembly.GetTypes()
            .Select(type => (Type: type, Command: type.GetInterfaces().FirstOrDefault(IsCommand)))
            .Where(candidate => candidate.Command is not null);

        Assert.All(commands, candidate =>
        {
            var handler = typeof(IRequestHandler<,>).MakeGenericType(candidate.Type, candidate.Command!.GenericTypeArguments[0]);
            Assert.Contains(services, descriptor => descriptor.ServiceType == handler);
        });
    }

    private static bool IsCommand(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommand<>);
}
