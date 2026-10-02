using System.Reflection;

namespace Knowledge.Domain;

/// <summary>
/// Marker type of the <c>Knowledge.Domain</c> assembly, the domain model of the Knowledge bounded context (ADR-0028):
/// materials, collections, categories and the user's library (favorites, completed materials).
/// </summary>
/// <remarks>
/// Use it wherever code needs a handle to this assembly without referencing a specific domain type. The write and read
/// <c>DbContext</c>s return it as their <c>DomainAssembly</c>, so that the framework conventions discover all strongly typed IDs
/// and single-value objects declared here and map them automatically (ADR-0023, ADR-0024). The domain tests use it to check that
/// this assembly references nothing but <c>SuperApp.Framework.Domain</c> and the base class library.
/// </remarks>
/// <example>
/// <code>
/// // Knowledge.Infrastructure, KnowledgeWriteDbContext
/// protected override Assembly DomainAssembly =&gt; KnowledgeDomain.Assembly;
/// </code>
/// </example>
public static class KnowledgeDomain
{
    /// <summary>Gets the assembly that contains the domain model of the Knowledge context.</summary>
    public static Assembly Assembly => typeof(KnowledgeDomain).Assembly;
}
