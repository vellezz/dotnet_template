using Knowledge.Infrastructure.Persistence.Read.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Knowledge.Infrastructure.Persistence.Read.Configurations;

/// <summary>
/// Maps <see cref="MaterialCategoryRow"/> onto the existing table <c>knowledge.MaterialCategories</c> for reading; the composite key is (<c>MaterialId</c>, <c>CategoryId</c>).
/// </summary>
/// <remarks>
/// Read configurations only describe the table and key so that EF Core can query it; they never create schema, because the read context
/// has no migrations. The table itself (columns, constraints, indexes) is defined by the write-side configuration.
/// </remarks>
internal sealed class MaterialCategoryRowConfiguration : IEntityTypeConfiguration<MaterialCategoryRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MaterialCategoryRow> builder) =>
        builder.ToTable("MaterialCategories").HasKey(row => new { row.MaterialId, row.CategoryId });
}
