using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.CreateMaterial;

/// <summary>
/// Creates a material (article, video or podcast) as a draft without content and categories.
/// Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>
/// Input rules (<c>CreateMaterialValidator</c>): <see cref="Type"/> is a defined value; <see cref="Title"/> not blank, at most
/// <see cref="Material.MaxTitleLength"/> characters after trimming; <see cref="Description"/> at most <see cref="Material.MaxDescriptionLength"/>
/// characters after trimming. The main media rules are checked by the aggregate.
/// </para>
/// <para>
/// The handler creates the <c>Material</c> aggregate in status <c>Draft</c> and adds it to the repository. Typical next steps:
/// <c>ReplaceMaterialContent</c>, <c>SetMaterialCategories</c>, then <c>PublishMaterial</c>. A video or podcast may be created without
/// main media, but it cannot be published until the media URL is set with <c>UpdateMaterialDetails</c>.
/// </para>
/// <para>Result: on success the identifier of the new material. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): the input rules above.</description></item>
///   <item><description><see cref="MaterialErrors.MediaNotAllowedForArticle"/> (<c>knowledge.material.media_not_allowed</c>, 400):
///   a main media URL was given for an article.</description></item>
///   <item><description><c>knowledge.url.invalid</c> (400): the main media URL is not an absolute <c>https</c> URL of at most
///   <see cref="Knowledge.Domain.Common.WebUrl.MaxLength"/> characters.</description></item>
///   <item><description><see cref="MaterialErrors.InvalidDuration"/> (<c>knowledge.material.invalid_duration</c>, 400): negative duration, or a
///   duration without a main media URL.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var id = await sender.Send(
///     new CreateMaterial(MaterialType.Podcast, "Sleep and stress", null, "https://cdn.example.com/ep12.mp3", 1860),
///     cancellationToken);
/// </code>
/// </example>
/// <param name="Type">Kind of material: <c>Article</c>, <c>Video</c> or <c>Podcast</c>. Cannot be changed later.</param>
/// <param name="Title">Title of the material; not blank, at most <see cref="Material.MaxTitleLength"/> characters after trimming. Stored trimmed.</param>
/// <param name="Description">
/// Optional short description (lead), at most <see cref="Material.MaxDescriptionLength"/> characters after trimming; <see langword="null"/> or blank means
/// no description. Stored trimmed.
/// </param>
/// <param name="MainMediaUrl">
/// Address of the main recording of a video or podcast (absolute <c>https</c> URL); <see langword="null"/> when not known yet.
/// Must be <see langword="null"/> for an article.
/// </param>
/// <param name="MainMediaDurationSeconds">
/// Length of the main recording in whole seconds, not negative; <see langword="null"/> when unknown. Allowed only together with
/// <paramref name="MainMediaUrl"/>.
/// </param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record CreateMaterial(
    MaterialType Type,
    string Title,
    string? Description,
    string? MainMediaUrl,
    int? MainMediaDurationSeconds) : ICommand<Result<Guid>>;
