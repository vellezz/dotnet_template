using Knowledge.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Read.Configurations;

/// <summary>
/// Maps <see cref="ContentBlockRow"/> onto the existing table <c>knowledge.ContentBlocks</c> for reading; the key is <c>Id</c>.
/// </summary>
/// <remarks>
/// Read configurations only describe the table and key so that EF Core can query it; they never create schema, because the read context
/// has no migrations. The table itself (columns, constraints, indexes) is defined by the write-side configuration.
/// </remarks>
internal sealed class ContentBlockRowConfiguration : IEntityTypeConfiguration<ContentBlockRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentBlockRow> builder) => builder.ToTable("ContentBlocks").HasKey(row => row.Id);
}
