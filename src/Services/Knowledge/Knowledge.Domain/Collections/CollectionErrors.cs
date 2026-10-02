using SuperApp.Framework.Domain.Results;

namespace Knowledge.Domain.Collections;

/// <summary>
/// Catalog of reusable errors for the <see cref="Collection"/> aggregate and the use cases around it (ADR-0015).
/// </summary>
/// <remarks>
/// Aggregate methods and handlers return these fields directly; the API maps the <see cref="ErrorType"/> to the HTTP status
/// (Validation 400, NotFound 404, BusinessRule 422). Codes are a public contract and must not change. Errors created inline elsewhere:
/// <c>knowledge.collection.invalid_title</c>, <c>knowledge.collection.invalid_description</c> (<see cref="Collection.UpdateDetails"/>)
/// and <c>knowledge.collection.invalid_id</c> (<see cref="Knowledge.Domain.Collections.CollectionId.Create"/>).
/// </remarks>
public static class CollectionErrors
{
    /// <summary>
    /// <c>knowledge.collection.not_found</c> (NotFound, HTTP 404): no collection with the given ID exists, the ID is empty, or (for readers
    /// without the catalog write scope) the collection is not published and therefore not visible. Returned by the handlers.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound("knowledge.collection.not_found", "Kolekcja nie istnieje.");

    /// <summary>
    /// <c>knowledge.collection.archived</c> (BusinessRule, HTTP 422): an attempt to change or publish an archived collection. Returned by
    /// <see cref="Collection.UpdateDetails"/>, <see cref="Collection.SetItems"/>, <see cref="Collection.SetCategories"/> and
    /// <see cref="Collection.Publish"/>.
    /// </summary>
    public static readonly Error Archived = Error.BusinessRule("knowledge.collection.archived", "Kolekcja jest zarchiwizowana i nie może być zmieniana.");

    /// <summary>
    /// <c>knowledge.collection.items_required</c> (BusinessRule, HTTP 422): publishing a collection without items, or setting an empty item
    /// list on a published collection. Returned by <see cref="Collection.Publish"/> and <see cref="Collection.SetItems"/>.
    /// </summary>
    public static readonly Error ItemsRequired = Error.BusinessRule("knowledge.collection.items_required", "Opublikowana kolekcja wymaga co najmniej jednego materiału.");

    /// <summary>
    /// <c>knowledge.collection.too_many_items</c> (Validation, HTTP 400): more than <see cref="Collection.MaxItems"/> materials were given.
    /// Returned by <see cref="Collection.SetItems"/>, which checks duplicates first, so the count is always a count of distinct materials.
    /// </summary>
    public static readonly Error TooManyItems =
        Error.Validation("knowledge.collection.too_many_items", $"Kolekcja może zawierać najwyżej {Collection.MaxItems} materiałów.");

    /// <summary>
    /// <c>knowledge.collection.duplicate_items</c> (Validation, HTTP 400): the same material appears more than once in the item list.
    /// Returned by <see cref="Collection.SetItems"/>.
    /// </summary>
    public static readonly Error DuplicateItems = Error.Validation("knowledge.collection.duplicate_items", "Materiał może wystąpić w kolekcji tylko raz.");

    /// <summary>
    /// <c>knowledge.collection.unknown_materials</c> (Validation, HTTP 400): at least one of the given material IDs is empty or does not
    /// exist. Returned by the set-items handler after <see cref="Knowledge.Domain.Materials.IMaterialRepository.AllExistAsync"/>.
    /// </summary>
    public static readonly Error UnknownMaterials = Error.Validation("knowledge.collection.unknown_materials", "Co najmniej jeden materiał nie istnieje.");

    /// <summary>
    /// <c>knowledge.collection.too_many_categories</c> (Validation, HTTP 400): more than <see cref="Collection.MaxCategories"/> distinct
    /// category IDs were given (duplicates are removed before counting). Returned by <see cref="Collection.SetCategories"/>.
    /// </summary>
    public static readonly Error TooManyCategories =
        Error.Validation("knowledge.collection.too_many_categories", $"Kolekcja może należeć najwyżej do {Collection.MaxCategories} kategorii.");
}
