using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Library.MarkMaterialCompleted;

/// <summary>
/// Marks a published material as completed (read, watched or listened to) by the calling user.
/// Requires scope <see cref="KnowledgeScopes.LibraryWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>MarkMaterialCompletedValidator</c>): <see cref="MaterialId"/> not empty.</para>
/// <para>
/// The handler takes the user from the token (<c>sub</c>), checks that the material is published and adds a <c>MaterialCompletion</c>
/// unless one already exists. The operation is idempotent: marking an already completed material succeeds and keeps the original
/// <c>CompletedAt</c>. Use <c>UnmarkMaterialCompleted</c> to undo.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403): no authenticated user or no <c>sub</c> claim; <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><c>knowledge.user.invalid_id</c> (400): the <c>sub</c> claim is longer than
///   <see cref="Knowledge.Domain.Common.UserId.MaxLength"/> characters.</description></item>
///   <item><description><see cref="Knowledge.Domain.Library.LibraryErrors.ItemNotAvailable"/> (<c>knowledge.library.item_not_available</c>, 404):
///   the material does not exist or is not published.</description></item>
///   <item><description><see cref="Knowledge.Domain.Library.LibraryErrors.CompletionRecordedConcurrently"/>
///   (<c>knowledge.library.completion_recorded_concurrently</c>, 409): only as the outcome of a race, when a concurrent request of the same
///   user marked the same material at the same moment. The completion exists afterwards; a retry succeeds.</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record MarkMaterialCompleted(Guid MaterialId) : ICommand;
