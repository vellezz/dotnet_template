using System.Reflection;

namespace SuperApp.Framework.Infrastructure.OpenApi;

/// <summary>
/// Detects that the application was started by the build-time OpenAPI generator rather than as a real service (ADR-0009).
/// </summary>
/// <remarks>
/// During <c>dotnet build</c> of an Api project, the <c>GetDocument.Insider</c> tool starts the application to produce the committed
/// <c>openapi/*.json</c> contract. No broker or secrets are available at that moment, so registration code checks <see cref="IsActive"/>
/// and skips infrastructure that would connect somewhere (MassTransit/RabbitMQ). Use it only for such registrations, never in request handling.
/// </remarks>
public static class BuildTimeDocumentGeneration
{
    /// <summary>Gets a value indicating whether the current process is the build-time OpenAPI document generator; computed once per process.</summary>
    public static bool IsActive { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
