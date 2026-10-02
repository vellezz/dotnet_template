using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Materials.PublishMaterial;

/// <summary>
/// Publishes a draft material so that readers can see it. Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>PublishMaterialValidator</c>): <see cref="MaterialId"/> not empty.</para>
/// <para>
/// The handler loads the material and calls <c>Material.Publish</c>, which requires content and, for a video or podcast, a main media URL.
/// On the first publication it sets <c>PublishedAt</c> and raises <c>MaterialPublished</c>, which <c>MaterialPublishedTranslator</c>
/// turns into the <c>MaterialPublishedV1</c> integration event (outbox, same transaction). The operation is idempotent: publishing a
/// published material succeeds, keeps <c>PublishedAt</c> and publishes no event.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>, 404).</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.Archived"/> (<c>knowledge.material.archived</c>, 422):
///   archived materials cannot be published again.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.ContentRequired"/> (<c>knowledge.material.content_required</c>, 422):
///   the material has no content blocks.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.MainMediaRequired"/>
///   (<c>knowledge.material.main_media_required</c>, 422): a video or podcast without a main media URL.</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material to publish; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record PublishMaterial(Guid MaterialId) : ICommand;
