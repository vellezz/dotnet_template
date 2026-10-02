using SuperApp.Framework.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.Domain.Entries;
using SleepDiary.IntegrationTests.Infrastructure;
using SleepDiary.Infrastructure.Persistence.Write;
using SuperApp.Framework.Testing;

namespace SleepDiary.IntegrationTests;

/// <summary>
/// Race between two requests recording the same day: both pass the handler's existence check, and the unique index on
/// <c>UserId</c> + <c>Date</c> rejects the second insert. The unit of work must return <see cref="SleepEntryErrors.AlreadyExists"/>
/// (HTTP 409), not throw (HTTP 500).
/// </summary>
public sealed class UniqueEntryPerDayTests(ServiceFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 9, 10);

    [Fact]
    public async Task Duplicate_day_on_save_is_returned_as_already_exists()
    {
        var user = UserId.FromTrusted("user-race");
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var first = fixture.Services.CreateAsyncScope())
        {
            first.ServiceProvider.GetRequiredService<ISleepEntryRepository>().Add(NewEntry(user));
            Assert.True((await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken)).IsSuccess);
        }

        await using var second = fixture.Services.CreateAsyncScope();
        second.ServiceProvider.GetRequiredService<ISleepEntryRepository>().Add(NewEntry(user));

        var result = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        Assert.Equal(SleepEntryErrors.AlreadyExists, result.Error);
        var db = second.ServiceProvider.GetRequiredService<SleepDiaryWriteDbContext>();
        Assert.Equal(1, await db.Set<SleepEntry>().CountAsync(entry => entry.UserId == user && entry.Date == Day, cancellationToken));
    }

    private static SleepEntry NewEntry(UserId user) =>
        ResultAssert.Success(SleepEntry.Record(
            user,
            Day,
            new SleepDetails(Day.AddDays(-1).ToDateTime(new TimeOnly(23, 0)), Day.ToDateTime(new TimeOnly(7, 0)), 15, 1, 4, null),
            Day,
            DateTimeOffset.UtcNow));
}
