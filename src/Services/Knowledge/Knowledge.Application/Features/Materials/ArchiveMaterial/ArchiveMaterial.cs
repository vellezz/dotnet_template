using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Materials.ArchiveMaterial;

/// <summary>
/// Archives a material: it disappears from the reader catalog and can no longer be changed. Archiving cannot be undone.
/// Requires scope <see cref="KnowledgeScopes.CatalogWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>ArchiveMaterialValidator</c>): <see cref="MaterialId"/> not empty.</para>
/// <para>
/// The handler loads the material and calls <c>Material.Archive</c>, which works from any status (draft or published). The first
/// archiving raises the <c>MaterialArchived</c> domain event, which <c>MaterialArchivedTranslator</c> turns into the
/// <c>MaterialArchivedV1</c> integration event (outbox, same transaction); the Worker then removes all users' favorites of the material.
/// <c>MaterialChanged</c> is raised as well and invalidates the cached material. The material stays in collections that contain it;
/// readers no longer see it there. Completion marks are kept.
/// The operation is idempotent: archiving an archived material succeeds and publishes nothing.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403), <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><see cref="Knowledge.Domain.Materials.MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>, 404).</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material to archive; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogWrite)]
public sealed record ArchiveMaterial(Guid MaterialId) : ICommand;
