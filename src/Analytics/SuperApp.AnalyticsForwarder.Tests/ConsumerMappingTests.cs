using SuperApp.AnalyticsForwarder.Consumers;
using SuperApp.AnalyticsForwarder.Events;
using SuperApp.AnalyticsForwarder.Tests.Fakes;
using Knowledge.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.Contracts;

namespace SuperApp.AnalyticsForwarder.Tests;

/// <summary>
/// Integration events become product events with allow-listed properties only; health data from SleepDiary never reaches analytics.
/// Runs the real consumers on the in-memory MassTransit test harness.
/// </summary>
public sealed class ConsumerMappingTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Material_published_is_a_system_event_with_identifier_and_type_but_no_title()
    {
        var captured = await ConsumeAsync(new MaterialPublishedV1(Guid.Parse("3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10"), "Article", "Higiena snu", At));

        Assert.Equal(ProductEventNames.KnowledgeMaterialPublished, captured.Name);
        Assert.Null(captured.Subject);
        Assert.Equal(At, captured.OccurredAt);
        Assert.NotNull(captured.MessageId);
        Assert.Equal(["material_id", "material_type"], captured.Properties.Keys.Order());
        Assert.Equal("Article", captured.Properties["material_type"]);
    }

    [Fact]
    public async Task Sleep_entry_recorded_belongs_to_the_user_and_carries_no_health_data()
    {
        var captured = await ConsumeAsync(new SleepEntryRecordedV1(Guid.NewGuid(), "user-sub-1", new DateOnly(2026, 9, 30), 465, 4, At));

        Assert.Equal(ProductEventNames.SleepDiaryEntryRecorded, captured.Name);
        Assert.Equal("user-sub-1", captured.Subject);
        Assert.Empty(captured.Properties);
    }

    [Fact]
    public async Task Archived_material_and_collection_carry_only_their_identifiers()
    {
        var materialId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();

        var material = await ConsumeAsync(new MaterialArchivedV1(materialId, At));
        var collection = await ConsumeAsync(new CollectionArchivedV1(collectionId, At));

        Assert.Equal(materialId.ToString(), Assert.Single(material.Properties).Value);
        Assert.Equal("material_id", Assert.Single(material.Properties).Key);
        Assert.Equal(collectionId.ToString(), Assert.Single(collection.Properties).Value);
        Assert.Equal("collection_id", Assert.Single(collection.Properties).Key);
    }

    private static async Task<ProductEvent> ConsumeAsync<TMessage>(TMessage message)
        where TMessage : class
    {
        var sink = new RecordingProductEventSink();
        await using var provider = new ServiceCollection()
            .AddSingleton<IProductEventSink>(sink)
            .AddMassTransitTestHarness(bus => bus.AddConsumers(typeof(MaterialPublishedConsumer).Assembly))
            .BuildServiceProvider(validateScopes: true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Bus.Publish(message, TestContext.Current.CancellationToken);

        Assert.True(await harness.Consumed.Any<TMessage>(TestContext.Current.CancellationToken));
        return Assert.Single(sink.Captured);
    }
}
