using Knowledge.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Read.Configurations;

/// <summary>
/// Maps <see cref="ContentTextSpanRow"/> onto the existing table <c>knowledge.ContentTextSpans</c> for reading; the composite key is (<c>BlockId</c>, <c>Position</c>).
/// </summary>
/// <remarks>
/// Read configurations only describe the table and key so that EF Core can query it; they never create schema, because the read context
/// has no migrations. The table itself (columns, constraints, indexes) is defined by the write-side configuration.
/// </remarks>
internal sealed class ContentTextSpanRowConfiguration : IEntityTypeConfiguration<ContentTextSpanRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentTextSpanRow> builder) =>
        builder.ToTable("ContentTextSpans").HasKey(row => new { row.BlockId, row.Position });
}
