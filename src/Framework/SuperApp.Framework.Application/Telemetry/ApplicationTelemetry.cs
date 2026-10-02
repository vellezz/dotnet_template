using System.Diagnostics;

namespace SuperApp.Framework.Application.Telemetry;

/// <summary>
/// OpenTelemetry activity source of the application layer. The logging pipeline behavior starts one span per command or query,
/// named after the request type (for example <c>CreateCategory</c>).
/// </summary>
/// <remarks>
/// The source is added to tracing by <c>AddAppServiceDefaults</c>; spans appear in Tempo between the ASP.NET Core (or MassTransit)
/// span and the SQL spans, with status <c>Error</c> and the error code when the request returns a failed result.
/// </remarks>
public static class ApplicationTelemetry
{
    /// <summary>Name of the activity source, to be passed to <c>AddSource</c> of the OpenTelemetry tracer provider.</summary>
    public const string SourceName = "SuperApp.Application";

    /// <summary>The activity source used by the logging behavior to create a span for every MediatR request.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);
}
