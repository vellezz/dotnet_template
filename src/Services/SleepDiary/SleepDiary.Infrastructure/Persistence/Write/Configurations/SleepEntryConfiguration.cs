using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SleepDiary.Domain.Entries;

namespace SleepDiary.Infrastructure.Persistence.Write.Configurations;

/// <summary>
/// EF Core mapping of the <see cref="SleepEntry"/> aggregate to the <c>sleepdiary.SleepEntries</c> table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SleepEntryId"/>, <see cref="UserId"/> and <see cref="SleepQuality"/> are converted to primitive columns by the <c>SuperApp.Framework</c>
/// conventions, so they need no explicit converters here.
/// </para>
/// <para>The database backs up the aggregate's invariants as a last line of defense:</para>
/// <list type="bullet">
///   <item><description>unique index <see cref="UserDateIndexName"/> on <c>UserId</c> + <c>Date</c>: one entry per user per day, also under
///   concurrent inserts; a violation is returned by the unit of work as <see cref="SleepEntryErrors.AlreadyExists"/>
///   (mapped in <see cref="SleepDiaryWriteDbContext"/>);</description></item>
///   <item><description><c>CHECK</c> constraints: wake after bed, quality range, awakenings range, latency within the time in bed;</description></item>
///   <item><description>bed and wake times stored as <c>datetime2(0)</c> (whole seconds, no time zone);</description></item>
///   <item><description>shadow row version <c>Version</c>: optimistic concurrency, a concurrent update of the same entry fails instead of overwriting.</description></item>
/// </list>
/// <para>Any change here requires a new migration of <see cref="SleepDiaryWriteDbContext"/>.</para>
/// </remarks>
internal sealed class SleepEntryConfiguration : IEntityTypeConfiguration<SleepEntry>
{
    /// <summary>
    /// Database name of the unique index on <c>UserId</c> + <c>Date</c> (<c>IX_SleepEntries_UserId_Date</c>, as created by the
    /// <c>Initial</c> migration). Set explicitly so that the name <see cref="SleepDiaryWriteDbContext"/> maps to
    /// <see cref="SleepEntryErrors.AlreadyExists"/> cannot drift silently; changing it requires a migration that renames the index.
    /// </summary>
    internal const string UserDateIndexName = "IX_SleepEntries_UserId_Date";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SleepEntry> builder)
    {
        builder.ToTable("SleepEntries", table =>
        {
            table.HasCheckConstraint("CK_SleepEntries_WakeAfterBed", "[WakeTime] > [BedTime]");
            table.HasCheckConstraint("CK_SleepEntries_Quality", $"[Quality] BETWEEN {SleepQuality.Min} AND {SleepQuality.Max}");
            table.HasCheckConstraint("CK_SleepEntries_Awakenings", $"[Awakenings] BETWEEN 0 AND {SleepEntry.MaxAwakenings}");
            table.HasCheckConstraint("CK_SleepEntries_Latency", "[SleepLatencyMinutes] >= 0 AND [SleepLatencyMinutes] <= [TimeInBedMinutes]");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.UserId).HasMaxLength(UserId.MaxLength);
        builder.Property(entry => entry.BedTime).HasColumnType("datetime2(0)");
        builder.Property(entry => entry.WakeTime).HasColumnType("datetime2(0)");
        builder.Property(entry => entry.Notes).HasMaxLength(SleepEntry.MaxNotesLength);
        builder.Property<byte[]>("Version").IsRowVersion();
        builder.HasIndex(entry => new { entry.UserId, entry.Date }).IsUnique().HasDatabaseName(UserDateIndexName);
        builder.Ignore(entry => entry.DomainEvents);
    }
}
