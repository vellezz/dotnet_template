using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Materials.UpdateMaterialDetails;

/// <summary>
/// Changes the descriptive data of a material: title, description and main media. The kind of material and its content stay unchanged.
/// Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>UpdateMaterialDetailsValidator</c>): <see cref="MaterialId"/> not empty; <see cref="Title"/> not blank, at most
/// <see cref="Knowledge.Domain.Materials.Material.MaxTitleLength"/> characters after trimming; <see cref="Description"/> at most
/// <see cref="Knowledge.Domain.Materials.Material.MaxDescriptionLength"/> characters after trimming. The main media rules are checked by the aggregate.
/// </para>
/// <para>
/// All four fields are replaced (this is not a partial update): send the current values to keep them. Allowed for drafts and published
/// materials; a published video or podcast must keep a main media URL.
/// </para>
/// <para>Result: success without a value. Possible errors, in the order they are checked:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>, 422).</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.MediaNotAllowedForArticle"/>
///   (<c>knowledge.material.media_not_allowed</c>, 400): a main media URL was given for an article.</description></item>
///   <item><description><c>knowledge.url.invalid</c> (400): the main media URL is not an absolute <c>https</c> URL of at most
///   <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.InvalidDuration"/> (<c>knowledge.material.invalid_duration</c>, 400):
///   negative duration, or a duration without a main media URL.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.MainMediaRequired"/>
///   (<c>knowledge.material.main_media_required</c>, 422): removing the main media of a published video or podcast.</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material; must not be <see cref="Guid.Empty"/>.</param>
/// <param name="Title">New title; not blank, at most <see cref="Knowledge.Domain.Materials.Material.MaxTitleLength"/> characters after trimming. Stored trimmed.</param>
/// <param name="Description">
/// New description, at most <see cref="Knowledge.Domain.Materials.Material.MaxDescriptionLength"/> characters after trimming; <see langword="null"/> or
/// blank removes the description. Stored trimmed.
/// </param>
/// <param name="MainMediaUrl">
/// Address of the main recording of a video or podcast (absolute <c>https</c> URL); <see langword="null"/> removes it (drafts only).
/// Must be <see langword="null"/> for an article.
/// </param>
/// <param name="MainMediaDurationSeconds">
/// Length of the main recording in whole seconds, not negative; <see langword="null"/> when unknown. Allowed only together with
/// <paramref name="MainMediaUrl"/>.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record UpdateMaterialDetails(
    Guid MaterialId,
    string Title,
    string? Description,
    string? MainMediaUrl,
    int? MainMediaDurationSeconds) : ICommand;
