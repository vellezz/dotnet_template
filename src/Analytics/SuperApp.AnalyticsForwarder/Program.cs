using SuperApp.AnalyticsForwarder.Events;
using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Framework.Infrastructure.Hosting;
using SuperApp.Framework.Infrastructure.Messaging;
using MassTransit;

// Analytics forwarder (ADR-0036): subscribes to integration events of the services and forwards allow-listed backend product events to
// PostHog Cloud EU. It has no database and no domain; services never talk to PostHog themselves, so a PostHog outage or a change of the
// analytics model never affects them. With analytics disabled (locally) the events are only logged.
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults("analytics-forwarder");
builder.AddAppWorker();
builder.Services.AddAppAnalytics(builder.Configuration);
builder.Services.AddProductEventSink(builder.Configuration);
builder.Services.AddAppEventSubscriber(builder.Configuration, "analytics", bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapAppDefaultEndpoints();

await app.RunAsync();

/// <summary>Entry point of the analytics forwarder; public for tests that start the host.</summary>
public partial class Program;
