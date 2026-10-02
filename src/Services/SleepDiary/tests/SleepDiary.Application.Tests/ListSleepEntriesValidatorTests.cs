using SleepDiary.Application.Features.Entries.ListSleepEntries;

namespace SleepDiary.Application.Tests;

/// <summary>
/// A missing <c>from</c>/<c>to</c> query parameter binds to <c>default(DateOnly)</c> (0001-01-01) and must be rejected; the range rules still apply.
/// </summary>
public sealed class ListSleepEntriesValidatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private readonly ListSleepEntriesValidator _validator = new();

    [Fact]
    public void Accepts_a_week() => Assert.True(_validator.Validate(new ListSleepEntries(Monday, Monday.AddDays(6))).IsValid);

    [Fact]
    public void Rejects_missing_from() =>
        Assert.Contains(_validator.Validate(new ListSleepEntries(default, Monday)).Errors, error => error.PropertyName == "From");

    [Fact]
    public void Rejects_missing_to() =>
        Assert.Contains(_validator.Validate(new ListSleepEntries(Monday, default)).Errors, error => error.PropertyName == "To");

    [Fact]
    public void Rejects_both_ends_missing()
    {
        var errors = _validator.Validate(new ListSleepEntries(default, default)).Errors;

        Assert.Contains(errors, error => error.PropertyName == "From");
        Assert.Contains(errors, error => error.PropertyName == "To");
    }

    [Fact]
    public void Rejects_reversed_range() => Assert.False(_validator.Validate(new ListSleepEntries(Monday, Monday.AddDays(-1))).IsValid);

    [Fact]
    public void Accepts_the_longest_range_and_rejects_a_longer_one()
    {
        Assert.True(_validator.Validate(new ListSleepEntries(Monday, Monday.AddDays(ListSleepEntriesValidator.MaxRangeDays - 1))).IsValid);
        Assert.False(_validator.Validate(new ListSleepEntries(Monday, Monday.AddDays(ListSleepEntriesValidator.MaxRangeDays))).IsValid);
    }
}
