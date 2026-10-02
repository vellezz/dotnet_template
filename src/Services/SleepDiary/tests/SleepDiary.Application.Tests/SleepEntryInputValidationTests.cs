using System.Text.Json;
using SleepDiary.Application.Features.Entries.RecordSleepEntry;
using SleepDiary.Application.Features.Entries.UpdateSleepEntry;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Application.Tests;

/// <summary>
/// The validators of the recording and update commands accept exactly what the aggregate accepts for the same field (limits and trimming
/// of the note) and reject wall-clock times that were sent with <c>Z</c> or an offset.
/// </summary>
public sealed class SleepEntryInputValidationTests
{
    private static readonly DateOnly Date = new(2026, 9, 30);
    private static readonly DateTime Bed = new(2026, 9, 29, 23, 0, 0);
    private static readonly DateTime Wake = new(2026, 9, 30, 7, 0, 0);

    [Fact]
    public void Notes_that_fit_only_after_trimming_are_accepted_like_in_the_aggregate()
    {
        var notes = "   " + new string('a', SleepEntry.MaxNotesLength) + "   ";

        Assert.True(new RecordSleepEntryValidator().Validate(Record(Bed, Wake, notes)).IsValid);
        Assert.True(new UpdateSleepEntryValidator().Validate(Update(Bed, Wake, notes)).IsValid);
        Assert.True(SleepEntry.Record(UserId.FromTrusted("user-1"), Date, Details(notes), Date, DateTimeOffset.UtcNow).IsSuccess);
    }

    [Fact]
    public void Notes_longer_than_the_limit_after_trimming_are_rejected()
    {
        var notes = " " + new string('a', SleepEntry.MaxNotesLength + 1) + " ";

        Assert.Contains(new RecordSleepEntryValidator().Validate(Record(Bed, Wake, notes)).Errors, error => error.PropertyName == "Notes");
        Assert.Contains(new UpdateSleepEntryValidator().Validate(Update(Bed, Wake, notes)).Errors, error => error.PropertyName == "Notes");
    }

    [Fact]
    public void Wall_clock_times_without_offset_are_accepted()
    {
        var bed = Deserialize("\"2026-09-29T23:00:00\"");
        var wake = Deserialize("\"2026-09-30T07:00:00\"");

        Assert.True(new RecordSleepEntryValidator().Validate(Record(bed, wake, null)).IsValid);
        Assert.True(new UpdateSleepEntryValidator().Validate(Update(bed, wake, null)).IsValid);
    }

    [Theory]
    [InlineData("\"2026-09-30T07:00:00Z\"")]
    [InlineData("\"2026-09-30T07:00:00+02:00\"")]
    [InlineData("\"2026-09-30T07:00:00-05:00\"")]
    public void Times_sent_with_an_offset_are_rejected(string json)
    {
        var withOffset = Deserialize(json);

        Assert.Contains(new RecordSleepEntryValidator().Validate(Record(Bed, withOffset, null)).Errors, error => error.PropertyName == "WakeTime");
        Assert.Contains(new RecordSleepEntryValidator().Validate(Record(withOffset, Wake, null)).Errors, error => error.PropertyName == "BedTime");
        Assert.Contains(new UpdateSleepEntryValidator().Validate(Update(Bed, withOffset, null)).Errors, error => error.PropertyName == "WakeTime");
        Assert.Contains(new UpdateSleepEntryValidator().Validate(Update(withOffset, Wake, null)).Errors, error => error.PropertyName == "BedTime");
    }

    private static DateTime Deserialize(string json) => JsonSerializer.Deserialize<DateTime>(json);

    private static RecordSleepEntry Record(DateTime bed, DateTime wake, string? notes) => new(Date, bed, wake, 15, 1, 4, notes);

    private static UpdateSleepEntry Update(DateTime bed, DateTime wake, string? notes) => new(Date, bed, wake, 15, 1, 4, notes);

    private static SleepDetails Details(string? notes) => new(Bed, Wake, 15, 1, 4, notes);
}
