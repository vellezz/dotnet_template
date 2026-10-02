namespace Knowledge.Infrastructure.Persistence.Read.Models;

// Flat read models mapped onto the tables owned by the write side; never domain entities (ADR-0003, ADR-0026).

/// <summary>Read model of one row of the <c>knowledge.Categories</c> table (the <c>Category</c> aggregate).</summary>
/// <remarks>
/// Read models are plain classes with <c>init</c> properties, mapped by <see cref="KnowledgeReadDbContext"/> onto the tables created by the
/// migrations of the write context. They contain only the columns queries need; columns such as the concurrency token are not mapped.
/// </remarks>
internal sealed class CategoryRow
{
    /// <summary>Identifier of the category (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Display name, at most 100 characters.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Unique URL-friendly identifier (lowercase letters, digits, hyphens).</summary>
    public string Slug { get; init; } = string.Empty;
}
