using Knowledge.Application.IntegrationEvents;
using Knowledge.Application.Tests.Fakes;
using Knowledge.Contracts;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Collections.Events;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Materials.Content;
using Knowledge.Domain.Materials.Events;
using SuperApp.Framework.Testing;

namespace Knowledge.Application.Tests;

/// <summary>Integration events carry the timestamps recorded by the aggregates, never the time of translation.</summary>
public sealed class IntegrationEventTranslatorTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Changed = new(2026, 9, 2, 9, 30, 15, TimeSpan.Zero);

    private readonly FakeIntegrationEventPublisher _publisher = new();

    [Fact]
    public async Task Material_published_carries_the_aggregate_publication_time()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Higiena snu", null, null, null, Created));
        Assert.True(material.ReplaceContent([new BlockSpec(BlockType.Paragraph, Text: [new SpanSpec("Treść")])], Created).IsSuccess);
        Assert.True(material.Publish(Changed).IsSuccess);
        var domainEvent = material.DomainEvents.OfType<MaterialPublished>().Single();

        await new MaterialPublishedTranslator(_publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        var published = Assert.IsType<MaterialPublishedV1>(Assert.Single(_publisher.Published));
        Assert.Equal(new MaterialPublishedV1(material.Id.Value, "Article", "Higiena snu", Changed), published);
        Assert.Equal(material.PublishedAt, published.PublishedAt);
    }

    [Fact]
    public async Task Material_archived_carries_the_aggregate_archiving_time()
    {
        var material = ResultAssert.Success(Material.Create(MaterialType.Article, "Higiena snu", null, null, null, Created));
        Assert.True(material.Archive(Changed).IsSuccess);
        var domainEvent = material.DomainEvents.OfType<MaterialArchived>().Single();

        await new MaterialArchivedTranslator(_publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        Assert.Equal(new MaterialArchivedV1(material.Id.Value, Changed), Assert.Single(_publisher.Published));
    }

    [Fact]
    public async Task Collection_archived_carries_the_aggregate_archiving_time()
    {
        var collection = ResultAssert.Success(Collection.Create("Kurs snu", null, Created));
        Assert.True(collection.Archive(Changed).IsSuccess);
        var domainEvent = collection.DomainEvents.OfType<CollectionArchived>().Single();

        await new CollectionArchivedTranslator(_publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        Assert.Equal(new CollectionArchivedV1(collection.Id.Value, Changed), Assert.Single(_publisher.Published));
    }
}
