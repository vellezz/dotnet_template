using SuperApp.Framework.Application.Messaging;
using ServiceName.Application.Tests.Fakes;
using SuperApp.Framework.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceName.Application.Tests;

/// <summary>
/// Checks that the Application layer of the service is wired correctly into the MediatR pipeline.
/// </summary>
/// <remarks>
/// Put the handler tests of your use cases next to this class, one test class per use case: build the handler with fakes of its ports
/// (<see cref="FakeClock"/>, <see cref="FakeCurrentUser"/>, <see cref="FakeIntegrationEventPublisher"/>, in-memory repositories) and assert the
/// returned <c>Result</c> and the published events. Business rules themselves are tested in the Domain tests.
/// </remarks>
public sealed class PipelineRegistrationTests
{
    /// <summary>
    /// Every command of the service has a handler registered from the Application assembly. Queries are not checked here, because their
    /// handlers live in Infrastructure (ADR-0026). The test passes trivially while the service has no commands.
    /// </summary>
    [Fact]
    public void Every_command_has_a_registered_handler()
    {
        var services = new ServiceCollection();
        services.AddAppApplication(ServiceNameApplication.Assembly);

        var commands = ServiceNameApplication.Assembly.GetTypes()
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
