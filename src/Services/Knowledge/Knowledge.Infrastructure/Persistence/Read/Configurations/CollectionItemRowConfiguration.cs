using Knowledge.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Read.Configurations;

/// <summary>
/// Maps <see cref="CollectionItemRow"/> onto the existing table <c>knowledge.CollectionItems</c> for reading; the composite key is (<c>CollectionId</c>, <c>Position</c>).
/// </summary>
/// <remarks>
/// Read configurations only describe the table and key so that EF Core can query it; they never create schema, because the read context
/// has no migrations. The table itself (columns, constraints, indexes) is defined by the write-side configuration.
/// </remarks>
internal sealed class CollectionItemRowConfiguration : IEntityTypeConfiguration<CollectionItemRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CollectionItemRow> builder) =>
        builder.ToTable("CollectionItems").HasKey(row => new { row.CollectionId, row.Position });
}
