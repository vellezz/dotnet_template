using SuperApp.AnalyticsForwarder.Events;
using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Options;
using PostHog;

// Analytics forwarder (ADR-0036): subscribes to integration events of the services and forwards allow-listed backend product events to
// PostHog Cloud EU. It has no database and no domain; services never talk to PostHog themselves, so a PostHog outage or a change of the
// analytics model never affects them. With analytics disabled (locally) the events are only logged.
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults("analytics-forwarder");
builder.AddAppWorker();
builder.Services.AddAppAnalytics(builder.Configuration);
builder.Services.AddProductEventSink(builder.Configuration);
builder.Services.AddAppEventSubscriber(builder.Configuration, "analytics", bus => bus.AddConsumers(typeof(Program).Assembly));

builder.Services.AddSingleton<FeatureFlagsCache>();
var analyticsSettings = builder.Configuration.GetSection(AnalyticsOptions.SectionName).Get<AnalyticsOptions>();
if (analyticsSettings?.Enabled == true)
{
    builder.Services.AddHostedService<FeatureFlagsRefreshWorker>();
}

var app = builder.Build();

app.MapAppDefaultEndpoints();

app.MapGet("/internal/flags", async (string? distinctId, FeatureFlagsCache cache, IServiceProvider sp, IOptions<AnalyticsOptions> options, CancellationToken ct) =>
{
    var id = distinctId ?? AnalyticsIdentity.System;
    var cached = cache.GetFlags(id);
    if (cached.Count > 0 && id == AnalyticsIdentity.System)
    {
        return Results.Ok(cached);
    }

    var client = sp.GetService<IPostHogClient>();
    if (client is not null && id != AnalyticsIdentity.System)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.Value.FeatureFlagsTimeout);
            var evaluations = await client.EvaluateFlagsAsync(id, new AllFeatureFlagsOptions { DisableGeoIp = true }, timeout.Token);
            if (evaluations is not null)
            {
                var dictionary = evaluations.Keys.ToDictionary(k => k, k => evaluations.IsEnabled(k), StringComparer.OrdinalIgnoreCase);
                cache.SetUserFlags(id, dictionary, TimeSpan.FromSeconds(60));
                return Results.Ok(dictionary);
            }
        }
        catch
        {
            // Fallback to baseline system flags when individual evaluation fails or times out
        }
    }

    return Results.Ok(cached);
});

await app.RunAsync();

/// <summary>Entry point of the analytics forwarder; public for tests that start the host.</summary>
public partial class Program;
