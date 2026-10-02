using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace Knowledge.Application.Features.Library.UnmarkMaterialCompleted;

/// <summary>
/// Removes the calling user's "completed" mark from a material. Requires scope <see cref="KnowledgeScopes.LibraryWrite"/>.
/// </summary>
/// <remarks>
/// <para>Input rules (<c>UnmarkMaterialCompletedValidator</c>): <see cref="MaterialId"/> not empty.</para>
/// <para>
/// The handler takes the user from the token (<c>sub</c>) and deletes that user's <c>MaterialCompletion</c> for the material. The operation
/// is idempotent: when there is no mark (or no such material) it succeeds without a change. The material's status is not checked, so marks
/// of archived materials can be removed as well.
/// </para>
/// <para>Result: success without a value. Possible errors:</para>
/// <list type="bullet">
///   <item><description><c>auth.unauthenticated</c> (403): no authenticated user or no <c>sub</c> claim; <c>auth.missing_scope</c> (403).</description></item>
///   <item><description><c>validation.failed</c> (400): empty identifier.</description></item>
///   <item><description><c>knowledge.user.invalid_id</c> (400): the <c>sub</c> claim is longer than
///   <see cref="Knowledge.Domain.Common.UserId.MaxLength"/> characters.</description></item>
/// </list>
/// </remarks>
/// <param name="MaterialId">Identifier of the material; must not be <see cref="Guid.Empty"/>.</param>
[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record UnmarkMaterialCompleted(Guid MaterialId) : ICommand;
