using SleepDiary.Domain.Entries.Events;
using SleepDiary.Domain.Entries;
using SuperApp.Framework.Testing;

namespace SleepDiary.Domain.Tests;

public sealed class SleepEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 9, 30);
    private static readonly UserId User = UserId.FromTrusted("user-1");

    private static SleepDetails Details(
        DateTime? bed = null,
        DateTime? wake = null,
        int latency = 15,
        int awakenings = 1,
        int quality = 4,
        string? notes = null) =>
        new(bed ?? new DateTime(2026, 9, 29, 23, 0, 0), wake ?? new DateTime(2026, 9, 30, 7, 0, 0), latency, awakenings, quality, notes);

    [Fact]
    public void Computes_time_in_bed_and_sleep_and_raises_event()
    {
        var entry = ResultAssert.Success(SleepEntry.Record(User, Date, Details(), Date, Now));

        Assert.Equal(480, entry.TimeInBedMinutes);
        Assert.Equal(465, entry.SleepMinutes);
        Assert.Single(entry.DomainEvents.OfType<SleepEntryRecorded>());
    }

    [Fact]
    public void Recorded_event_carries_the_creation_time_of_the_entry()
    {
        var entry = ResultAssert.Success(SleepEntry.Record(User, Date, Details(), Date, Now));

        var recorded = Assert.Single(entry.DomainEvents.OfType<SleepEntryRecorded>());
        Assert.Equal(Now, entry.CreatedAt);
        Assert.Equal(entry.CreatedAt, recorded.RecordedAt);
    }

    [Fact]
    public void Accepts_notes_that_fit_the_limit_only_after_trimming()
    {
        var notes = "  " + new string('a', SleepEntry.MaxNotesLength) + "  ";

        var entry = ResultAssert.Success(SleepEntry.Record(User, Date, Details(notes: notes), Date, Now));

        Assert.Equal(SleepEntry.MaxNotesLength, entry.Notes!.Length);
    }

    [Fact]
    public void Rejects_notes_longer_than_the_limit_after_trimming() =>
        Assert.Equal(
            SleepEntryErrors.NotesTooLong,
            SleepEntry.Record(User, Date, Details(notes: new string('a', SleepEntry.MaxNotesLength + 1)), Date, Now).Error);

    [Fact]
    public void Rejects_wake_before_bed() =>
        Assert.Equal(SleepEntryErrors.WakeBeforeBed, SleepEntry.Record(User, Date, Details(bed: new DateTime(2026, 9, 30, 8, 0, 0)), Date, Now).Error);

    [Fact]
    public void Rejects_wake_on_other_day_than_entry() =>
        Assert.Equal(
            SleepEntryErrors.WakeDateMismatch,
            SleepEntry.Record(User, Date, Details(bed: new DateTime(2026, 9, 28, 23, 0, 0), wake: new DateTime(2026, 9, 29, 7, 0, 0)), Date, Now).Error);

    [Fact]
    public void Rejects_more_than_24_hours_in_bed() =>
        Assert.Equal(SleepEntryErrors.TooLong, SleepEntry.Record(User, Date, Details(bed: new DateTime(2026, 9, 29, 6, 0, 0)), Date, Now).Error);

    [Fact]
    public void Rejects_latency_longer_than_time_in_bed() =>
        Assert.Equal(SleepEntryErrors.InvalidLatency, SleepEntry.Record(User, Date, Details(latency: 600), Date, Now).Error);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rejects_quality_outside_scale(int quality) =>
        Assert.Equal("sleepdiary.entry.invalid_quality", ResultAssert.Failure(SleepEntry.Record(User, Date, Details(quality: quality), Date, Now)).Code);

    [Fact]
    public void Rejects_future_date() =>
        Assert.Equal(SleepEntryErrors.FutureDate, SleepEntry.Record(User, Date, Details(), Date.AddDays(-1), Now).Error);

    [Fact]
    public void Update_recomputes_sleep()
    {
        var entry = ResultAssert.Success(SleepEntry.Record(User, Date, Details(), Date, Now));

        Assert.True(entry.Update(Details(latency: 60, notes: "  Hałas  "), Now).IsSuccess);

        Assert.Equal(420, entry.SleepMinutes);
        Assert.Equal("Hałas", entry.Notes);
    }
}
