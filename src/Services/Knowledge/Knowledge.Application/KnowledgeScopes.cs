namespace Knowledge.Application;

/// <summary>
/// OAuth scopes of the Knowledge service. They decide which commands and queries a caller may send.
/// </summary>
/// <remarks>
/// <para>
/// Scopes follow the convention <c>{service}.{resource}.{action}</c> (ADR-0012) and are issued by the CIAM in the access token
/// (claim <c>scope</c>). The Knowledge service defines two resources (ADR-0028):
/// </para>
/// <list type="bullet">
///   <item><description><c>catalog</c>: the shared catalog of materials, collections and categories, edited by editors.</description></item>
///   <item><description><c>library</c>: the personal data of the current user (favorites, completed materials).</description></item>
/// </list>
/// <para>
/// Every command and query declares its scope with <c>[RequiresScope(...)]</c>. The authorization pipeline behavior (ADR-0017) checks it
/// before validation and returns <c>auth.missing_scope</c> (HTTP 403) when the token does not contain it, or <c>auth.unauthenticated</c>
/// (HTTP 403) when there is no authenticated user. The gateway performs a coarse check per route as well; the service never relies on it.
/// </para>
/// <para>
/// <see cref="CatalogWrite"/> also changes what read queries return: <c>GetMaterial</c> and <c>GetCollection</c> show drafts and archived
/// items to callers that hold it, while plain readers see only published items.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RequiresScope(KnowledgeScopes.CatalogWrite)]
/// public sealed record PublishMaterial(Guid MaterialId) : ICommand;
/// </code>
/// </example>
public static class KnowledgeScopes
{
    /// <summary>
    /// Read the catalog (<c>knowledge.catalog.read</c>): published materials, collections and categories.
    /// </summary>
    public const string CatalogRead = "knowledge.catalog.read";

    /// <summary>
    /// Edit the catalog (<c>knowledge.catalog.write</c>): create, change, publish and archive materials, collections and categories.
    /// </summary>
    /// <remarks>
    /// Intended for editors. Read queries that return a single item also show drafts and archived items to callers holding this scope.
    /// Reading still requires <see cref="CatalogRead"/> as well, because queries declare only that scope.
    /// </remarks>
    public const string CatalogWrite = "knowledge.catalog.write";

    /// <summary>
    /// Read the caller's own library (<c>knowledge.library.read</c>): favorites and materials the current user has completed.
    /// </summary>
    public const string LibraryRead = "knowledge.library.read";

    /// <summary>
    /// Change the caller's own library (<c>knowledge.library.write</c>): add and remove favorites, mark materials as completed
    /// and undo that mark.
    /// </summary>
    /// <remarks>Library operations always act on the user identified by the token's <c>sub</c> claim; they cannot touch another user's data.</remarks>
    public const string LibraryWrite = "knowledge.library.write";
}
