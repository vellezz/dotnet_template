using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Security;
using System.Reflection;
using SuperApp.Framework.Application.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.Framework.Application;

/// <summary>
/// Registers the application layer of a service: MediatR with the standard pipeline, validators and domain event handlers.
/// </summary>
/// <remarks>
/// Everything a developer adds to the Application or Infrastructure project (commands, queries, handlers, validators, domain event handlers)
/// is discovered by assembly scanning; there is no per-type registration to remember. See <see cref="AddAppApplication"/> for the pipeline order.
/// </remarks>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR with the pipeline behaviors in the order defined by ADR-0017, all FluentValidation validators and
    /// all <see cref="IDomainEventHandler{TEvent}"/> implementations found in <paramref name="assemblies"/>.
    /// </summary>
    /// <remarks>
    /// <para>Pipeline order for every request (outermost first):</para>
    /// <list type="number">
    ///   <item><description>Logging and tracing.</description></item>
    ///   <item><description>Authorization (<see cref="RequiresScopeAttribute"/>, <see cref="AllowAnonymousRequestAttribute"/>).</description></item>
    ///   <item><description>Validation (FluentValidation, errors returned as <c>validation.failed</c>).</description></item>
    ///   <item><description>Transaction (commands only): save and commit on success.</description></item>
    /// </list>
    /// <para>
    /// Authorization runs before validation so that an unauthorized caller cannot probe validation rules.
    /// Services normally do not call this method directly: the Infrastructure composition root (<c>Add{Service}Core</c>) calls it
    /// with the Application assembly (commands, command handlers, validators, translators) and the Infrastructure assembly
    /// (query handlers, cache invalidation handlers).
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="assemblies">
    /// Assemblies scanned for request handlers, validators (including <see langword="internal"/> ones) and domain event handlers.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAppApplication(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblies(assemblies);
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces().Where(IsDomainEventHandler))
            {
                services.AddScoped(handlerInterface, type);
            }
        }

        return services;
    }

    private static bool IsDomainEventHandler(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>);
}
