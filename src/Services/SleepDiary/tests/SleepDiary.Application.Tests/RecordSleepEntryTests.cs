using SuperApp.Framework.Domain.Results;
using SleepDiary.Application.Tests.Fakes;
using SleepDiary.Application.Features.Entries.RecordSleepEntry;
using SleepDiary.Domain.Entries;
using SuperApp.Framework.Testing;

namespace SleepDiary.Application.Tests;

public sealed class RecordSleepEntryTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);
    private readonly FakeSleepEntryRepository _entries = new();

    [Fact]
    public async Task Records_entry_for_current_user()
    {
        var result = await Handler("user-1").Handle(Command(Today), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-1", Assert.Single(_entries.Items).UserId.Value);
    }

    [Fact]
    public async Task Rejects_second_entry_for_the_same_day()
    {
        Assert.True((await Handler("user-1").Handle(Command(Today), TestContext.Current.CancellationToken)).IsSuccess);

        var second = await Handler("user-1").Handle(Command(Today), TestContext.Current.CancellationToken);

        Assert.Equal(SleepEntryErrors.AlreadyExists, second.Error);
    }

    [Fact]
    public async Task Tolerates_one_day_ahead_for_time_zones_but_not_more()
    {
        var tomorrow = await Handler("user-1").Handle(Command(Today.AddDays(1)), TestContext.Current.CancellationToken);
        var dayAfter = await Handler("user-1").Handle(Command(Today.AddDays(2)), TestContext.Current.CancellationToken);

        Assert.True(tomorrow.IsSuccess);
        Assert.Equal(SleepEntryErrors.FutureDate, dayAfter.Error);
    }

    [Fact]
    public async Task Anonymous_user_is_rejected()
    {
        var result = await Handler(null).Handle(Command(Today), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Forbidden, ResultAssert.Failure(result).Type);
    }

    private RecordSleepEntryHandler Handler(string? subject) =>
        new(_entries, new FakeCurrentUser(subject), new FakeClock(new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero)));

    private static RecordSleepEntry Command(DateOnly date) =>
        new(date, date.AddDays(-1).ToDateTime(new TimeOnly(23, 0)), date.ToDateTime(new TimeOnly(7, 0)), 10, 0, 4, null);

    private sealed class FakeSleepEntryRepository : ISleepEntryRepository
    {
        public List<SleepEntry> Items { get; } = [];

        public Task<SleepEntry?> FindAsync(UserId userId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(entry => entry.UserId == userId && entry.Date == date));

        public Task<bool> ExistsAsync(UserId userId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(entry => entry.UserId == userId && entry.Date == date));

        public void Add(SleepEntry entry) => Items.Add(entry);

        public void Remove(SleepEntry entry) => Items.Remove(entry);
    }
}
