using Knowledge.Domain.Materials;

namespace Knowledge.Domain.Collections;

/// <summary>
/// One item of a <see cref="Collection"/>: a material at a given position. A value object owned by the collection and stored in the
/// <c>CollectionItems</c> table (keyed by collection and position).
/// </summary>
/// <remarks>Created only by <see cref="Collection.SetItems"/>, which renumbers all items on every call.</remarks>
/// <param name="MaterialId">Identifier of the material; appears at most once in a collection.</param>
/// <param name="Position">Zero-based position, following the order given to <see cref="Collection.SetItems"/>; positions are contiguous.</param>
public sealed record CollectionItem(MaterialId MaterialId, int Position);
