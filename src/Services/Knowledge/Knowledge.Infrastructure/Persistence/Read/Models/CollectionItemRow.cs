namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.CollectionItems</c>: a material at a given position in a collection.</summary>
internal sealed class CollectionItemRow
{
    /// <summary>Identifier of the collection (first part of the key).</summary>
    public Guid CollectionId { get; init; }

    /// <summary>Zero-based position of the material in the collection (second part of the key).</summary>
    public int Position { get; init; }

    /// <summary>Identifier of the material at this position; no foreign key to <c>Materials</c>.</summary>
    public Guid MaterialId { get; init; }
}
