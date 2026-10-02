using System.Reflection;

namespace Knowledge.Application;

/// <summary>
/// Marker for the Application assembly of the Knowledge context: gives registration code and tests a stable handle to the assembly
/// that contains the commands, queries, command handlers, validators and domain event handlers of the service.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddKnowledgeCore</c> in <c>Knowledge.Infrastructure</c> passes <see cref="Assembly"/> (together with the Infrastructure assembly,
/// which holds the query handlers, ADR-0026) to <c>AddAppApplication</c>. That call scans the assemblies and registers every MediatR
/// handler, every FluentValidation validator and every <c>IDomainEventHandler&lt;T&gt;</c> it finds, so a new use case placed in this
/// project needs no manual registration.
/// </para>
/// <para>The unit tests use the same property to build the real pipeline and to check that every command has a handler.</para>
/// </remarks>
/// <example>
/// <code>
/// services.AddAppApplication(KnowledgeApplication.Assembly, typeof(InfrastructureServiceCollectionExtensions).Assembly);
/// </code>
/// </example>
public static class KnowledgeApplication
{
    /// <summary>The <c>Knowledge.Application</c> assembly, to be scanned for handlers, validators and domain event handlers.</summary>
    public static Assembly Assembly => typeof(KnowledgeApplication).Assembly;
}
