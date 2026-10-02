using SleepDiary.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SleepDiary.Infrastructure.Persistence.Read.Configurations;

/// <summary>
/// Maps <see cref="SleepEntryRow"/> onto the existing <c>SleepEntries</c> table in the service schema (read side only).
/// </summary>
/// <remarks>
/// The table itself is created and changed by migrations of the write context (<c>SleepEntryConfiguration</c>). When a column is renamed there,
/// update the read model too, or queries will fail at runtime.
/// </remarks>
internal sealed class SleepEntryRowConfiguration : IEntityTypeConfiguration<SleepEntryRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SleepEntryRow> builder) => builder.ToTable("SleepEntries").HasKey(row => row.Id);
}
