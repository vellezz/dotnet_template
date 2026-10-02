using SleepDiary.Application.IntegrationEvents;
using SleepDiary.Application.Tests.Fakes;
using SleepDiary.Contracts;
using SleepDiary.Domain.Entries;
using SleepDiary.Domain.Entries.Events;
using SuperApp.Framework.Testing;

namespace SleepDiary.Application.Tests;

/// <summary><see cref="SleepEntryRecordedV1.RecordedAt"/> is the entry's creation time carried by the domain event, not the time of translation.</summary>
public sealed class SleepEntryRecordedTranslatorTests
{
    [Fact]
    public async Task Integration_event_carries_the_creation_time_of_the_entry()
    {
        var createdAt = new DateTimeOffset(2026, 9, 30, 6, 15, 0, TimeSpan.Zero);
        var entry = ResultAssert.Success(SleepEntry.Record(
            UserId.FromTrusted("user-1"),
            new DateOnly(2026, 9, 30),
            new SleepDetails(new DateTime(2026, 9, 29, 23, 0, 0), new DateTime(2026, 9, 30, 7, 0, 0), 15, 1, 4, null),
            new DateOnly(2026, 10, 1),
            createdAt));
        var domainEvent = Assert.Single(entry.DomainEvents.OfType<SleepEntryRecorded>());
        var publisher = new FakeIntegrationEventPublisher();

        await new SleepEntryRecordedTranslator(publisher).HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        var published = Assert.IsType<SleepEntryRecordedV1>(Assert.Single(publisher.Published));
        Assert.Equal(createdAt, published.RecordedAt);
        Assert.Equal(entry.Id.Value, published.EntryId);
        Assert.Equal(entry.SleepMinutes, published.SleepMinutes);
    }
}
