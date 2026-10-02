using SuperApp.Framework.Domain.Results;

namespace Knowledge.Domain.Materials;

/// <summary>
/// Catalog of reusable errors for the <see cref="Material"/> aggregate and the use cases around it (ADR-0015).
/// </summary>
/// <remarks>
/// <para>
/// Each field is a single, shared definition of an error: aggregate methods and handlers return it (it converts implicitly to
/// <see cref="Result"/>), tests compare against it, and the API turns its <see cref="ErrorType"/> into the HTTP status
/// (Validation 400, NotFound 404, BusinessRule 422) with the <see cref="Error.Code"/> in the response. Codes are part of the public
/// contract and must never change; messages may.
/// </para>
/// <para>
/// Some material errors are created inline rather than listed here, because they are parametrized by field or path:
/// <c>knowledge.material.invalid_title</c>, <c>knowledge.material.invalid_description</c> (see <see cref="Material.UpdateDetails"/>),
/// <c>knowledge.material.invalid_id</c> (<see cref="Knowledge.Domain.Materials.MaterialId.Create"/>), <c>knowledge.url.invalid</c>
/// (<see cref="Knowledge.Domain.Common.WebUrl.Create"/>) and <c>knowledge.content.invalid_block</c>
/// (<see cref="Knowledge.Domain.Materials.Content.ContentBuilder.Build"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var material = await materials.GetAsync(materialId, cancellationToken);
/// return material is null ? MaterialErrors.NotFound : material.Archive(clock.UtcNow);
/// </code>
/// </example>
public static class MaterialErrors
{
    /// <summary>
    /// <c>knowledge.material.not_found</c> (NotFound, HTTP 404): no material with the given ID exists, the ID is empty, or (for readers
    /// without the catalog write scope) the material exists but is not published and therefore not visible.
    /// </summary>
    /// <remarks>Returned by command handlers after <see cref="IMaterialRepository.GetAsync"/> and by the <c>GetMaterial</c> query handler.</remarks>
    public static readonly Error NotFound = Error.NotFound("knowledge.material.not_found", "Materiał nie istnieje.");

    /// <summary>
    /// <c>knowledge.material.archived</c> (BusinessRule, HTTP 422): an attempt to change or publish an archived material.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="Material.UpdateDetails"/>, <see cref="Material.ReplaceContent"/>, <see cref="Material.SetCategories"/> and
    /// <see cref="Material.Publish"/>. Archiving is final, so retrying never helps. <see cref="Material.Archive"/> itself does not return it.
    /// </remarks>
    public static readonly Error Archived = Error.BusinessRule("knowledge.material.archived", "Materiał jest zarchiwizowany i nie może być zmieniany.");

    /// <summary>
    /// <c>knowledge.material.content_required</c> (BusinessRule, HTTP 422): publishing a material without content blocks, or replacing the
    /// content of a published material with an empty list.
    /// </summary>
    /// <remarks>Returned by <see cref="Material.Publish"/> and <see cref="Material.ReplaceContent"/>.</remarks>
    public static readonly Error ContentRequired = Error.BusinessRule("knowledge.material.content_required", "Opublikowany materiał wymaga treści.");

    /// <summary>
    /// <c>knowledge.material.main_media_required</c> (BusinessRule, HTTP 422): publishing a video or podcast without
    /// <see cref="Material.MainMediaUrl"/>, or removing the main media of a published video or podcast.
    /// </summary>
    /// <remarks>Returned by <see cref="Material.Publish"/> and <see cref="Material.UpdateDetails"/>.</remarks>
    public static readonly Error MainMediaRequired =
        Error.BusinessRule("knowledge.material.main_media_required", "Opublikowane wideo lub podcast wymaga głównego medium.");

    /// <summary>
    /// <c>knowledge.material.media_not_allowed</c> (Validation, HTTP 400): a main media URL (any non-null value) was given for an
    /// <see cref="MaterialType.Article"/>.
    /// </summary>
    /// <remarks>Returned by <see cref="Material.Create"/> and <see cref="Material.UpdateDetails"/>.</remarks>
    public static readonly Error MediaNotAllowedForArticle =
        Error.Validation("knowledge.material.media_not_allowed", "Artykuł nie ma głównego medium.");

    /// <summary>
    /// <c>knowledge.material.invalid_duration</c> (Validation, HTTP 400): the main media duration is negative, or a duration was given
    /// without a main media URL.
    /// </summary>
    /// <remarks>Returned by <see cref="Material.Create"/> and <see cref="Material.UpdateDetails"/>.</remarks>
    public static readonly Error InvalidDuration =
        Error.Validation("knowledge.material.invalid_duration", "Czas trwania musi być nieujemny i dotyczy wyłącznie głównego medium.");

    /// <summary>
    /// <c>knowledge.material.too_many_categories</c> (Validation, HTTP 400): more than <see cref="Material.MaxCategories"/>
    /// distinct category IDs were given (duplicates are removed before counting).
    /// </summary>
    /// <remarks>Returned by <see cref="Material.SetCategories"/>.</remarks>
    public static readonly Error TooManyCategories =
        Error.Validation("knowledge.material.too_many_categories", $"Materiał może należeć najwyżej do {Material.MaxCategories} kategorii.");
}
