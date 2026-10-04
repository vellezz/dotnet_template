using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.IntegrationTests.Infrastructure;
using SleepDiary.Infrastructure.Persistence.Write;
using SleepDiary.Application;
using SleepDiary.Application.Features.Entries.DeleteSleepEntry;
using SleepDiary.Application.Features.Entries.GetSleepEntry;
using SleepDiary.Application.Features.Entries.ListSleepEntries;
using SleepDiary.Application.Features.Entries.RecordSleepEntry;
using SleepDiary.Application.Features.Entries.UpdateSleepEntry;
using SleepDiary.Contracts;
using SleepDiary.Domain.Entries;
using SuperApp.Framework.Testing;

namespace SleepDiary.IntegrationTests;

/// <summary>End-to-end flows on MSSQL (Testcontainers); the tests of this class run sequentially.</summary>
[Trait("Category", "Integration")]
public sealed class SleepDiaryFlowTests(ServiceFixture fixture)
{
    private static readonly DateOnly Monday = new(2026, 9, 21);

    [Fact]
    public async Task Entries_are_recorded_updated_listed_and_deleted()
    {
        Act("user-a", SleepDiaryScopes.EntryWrite, SleepDiaryScopes.EntryRead);
        var recorded = ResultAssert.Success(await fixture.SendAsync(Record(Monday, quality: 3)));
        Assert.True((await fixture.SendAsync(Record(Monday.AddDays(1), quality: 5))).IsSuccess);

        Assert.Contains(fixture.Publisher.Published, message => message is SleepEntryRecordedV1 entry && entry.EntryId == recorded);

        Assert.True((await fixture.SendAsync(new UpdateSleepEntry(
            Monday, At(Monday.AddDays(-1), 22), At(Monday, 6), 30, 2, 4, "Po aktualizacji"))).IsSuccess);

        var diary = ResultAssert.Success(await fixture.SendAsync(new ListSleepEntries(Monday, Monday.AddDays(6))));
        Assert.Equal(2, diary.Entries.Count);
        Assert.Equal(4.5, diary.AverageQuality);

        Assert.True((await fixture.SendAsync(new DeleteSleepEntry(Monday))).IsSuccess);
        Assert.Equal(SleepEntryErrors.NotFound, (await fixture.SendAsync(new GetSleepEntry(Monday))).Error);
    }

    [Fact]
    public async Task Second_entry_for_the_same_day_is_rejected()
    {
        Act("user-b", SleepDiaryScopes.EntryWrite);
        Assert.True((await fixture.SendAsync(Record(Monday, quality: 3))).IsSuccess);

        Assert.Equal(SleepEntryErrors.AlreadyExists, (await fixture.SendAsync(Record(Monday, quality: 4))).Error);
    }

    [Fact]
    public async Task User_sees_only_own_entries()
    {
        Act("user-c", SleepDiaryScopes.EntryWrite);
        Assert.True((await fixture.SendAsync(Record(Monday.AddDays(3), quality: 2))).IsSuccess);

        Act("user-d", SleepDiaryScopes.EntryRead);
        var diary = ResultAssert.Success(await fixture.SendAsync(new ListSleepEntries(Monday, Monday.AddDays(6))));

        Assert.Empty(diary.Entries);
        Assert.Null(diary.AverageSleepMinutes);
    }

    [Fact]
    public async Task Listing_without_range_is_rejected_by_validation()
    {
        Act("user-e", SleepDiaryScopes.EntryRead);

        var result = await fixture.SendAsync(new ListSleepEntries(default, default));

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
    }

    [Fact]
    public async Task Recorded_event_carries_the_stored_creation_time()
    {
        Act("user-f", SleepDiaryScopes.EntryWrite);
        var recorded = ResultAssert.Success(await fixture.SendAsync(Record(Monday, quality: 4)));

        await using var scope = fixture.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SleepDiaryWriteDbContext>().Set<SleepEntry>()
            .SingleAsync(entry => entry.Id == SleepEntryId.FromTrusted(recorded), TestContext.Current.CancellationToken);
        var published = fixture.Publisher.Published.OfType<SleepEntryRecordedV1>().Single(message => message.EntryId == recorded);

        Assert.Equal(stored.CreatedAt, published.RecordedAt);
    }

    private static RecordSleepEntry Record(DateOnly date, int quality) =>
        new(date, At(date.AddDays(-1), 23), At(date, 7), 15, 1, quality, null);

    private static DateTime At(DateOnly date, int hour) => date.ToDateTime(new TimeOnly(hour, 0));

    private void Act(string subject, params string[] scopes)
    {
        fixture.CurrentUser.Subject = subject;
        fixture.CurrentUser.Scopes.Clear();
        fixture.CurrentUser.Scopes.UnionWith(scopes);
    }
}
