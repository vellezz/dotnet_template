using Microsoft.CodeAnalysis;

namespace SuperApp.Analyzers;

/// <summary>
/// Diagnostic descriptors (ID, title, message, severity) of all rules shipped by <c>SuperApp.Analyzers</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is an <see cref="DiagnosticSeverity.Error"/>, so a violation fails the build of every project in the solution
/// (the analyzer is attached to all projects except the analyzers themselves by <c>Directory.Build.props</c>).
/// The title and message are what the developer sees in the IDE and in the build output, so they name the ADR that explains the rule.
/// </para>
/// <para>
/// When adding a rule: add a descriptor here with the next free <c>APPxxx</c> ID, list it in <c>AnalyzerReleases.Unshipped.md</c>
/// (release tracking is enforced by <c>Microsoft.CodeAnalysis.Analyzers</c>), and cover it in <c>SuperApp.Analyzers.Tests</c>.
/// </para>
/// </remarks>
internal static class Descriptors
{
    private const string Category = "Architecture";

    /// <summary>
    /// APP001, reported by <see cref="ResultUsageAnalyzer"/>: a method returning <c>Result</c> / <c>Result&lt;T&gt;</c> is called
    /// and its value is discarded. Placeholder <c>{0}</c>: the discarded type as written in code.
    /// </summary>
    public static readonly DiagnosticDescriptor IgnoredResult = new(
        id: "APP001",
        title: "Ignored Result",
        messageFormat: "The result of type '{0}' is ignored; handle the error or discard it explicitly (ADR-0015)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// APP002, reported by <see cref="SingleValueObjectCreationAnalyzer"/>: a strongly typed ID or single-value object is created with
    /// <c>default</c> or a parameterless <c>new()</c>, bypassing its factories. Placeholder <c>{0}</c>: the type name.
    /// </summary>
    public static readonly DiagnosticDescriptor UninitializedValueObject = new(
        id: "APP002",
        title: "Uninitialized ID or value object",
        messageFormat: "'{0}' must not be created with default or new(); use a factory method (ADR-0023, ADR-0024)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// APP003, reported by <see cref="BannedApiAnalyzer"/>: use of an API forbidden by ADR-0027.
    /// Placeholders: <c>{0}</c> the banned API, <c>{1}</c> the reason including what to use instead.
    /// </summary>
    public static readonly DiagnosticDescriptor BannedApi = new(
        id: "APP003",
        title: "Banned API",
        messageFormat: "Use of '{0}' is not allowed: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// APP004, reported by <see cref="TypePerFileAnalyzer"/>: a second (third, ...) top-level type in one file (ADR-0032).
    /// Placeholder <c>{0}</c>: the name of the type that must be moved to its own file.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleTypesInFile = new(
        id: "APP004",
        title: "More than one type in a file",
        messageFormat: "Type '{0}' must be in its own file: one type per file (ADR-0032)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// APP005, reported by <see cref="TypePerFileAnalyzer"/>: the file name does not match the first top-level type (ADR-0032).
    /// Placeholders: <c>{0}</c> the type name, <c>{1}</c> the current file name without extension.
    /// </summary>
    public static readonly DiagnosticDescriptor FileNameMismatch = new(
        id: "APP005",
        title: "File name does not match the type",
        messageFormat: "Type '{0}' must be in file '{0}.cs' (currently '{1}.cs') (ADR-0032)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// APP006, reported by <see cref="DocumentationCompletenessAnalyzer"/>: XML documentation of a public member lacks required tags.
    /// Placeholders: <c>{0}</c> the symbol name, <c>{1}</c> the comma-separated list of missing tags, e.g. <c>&lt;param name="id"&gt;, &lt;returns&gt;</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompleteDocumentation = new(
        id: "APP006",
        title: "Incomplete XML documentation",
        messageFormat: "The documentation of '{0}' does not describe: {1} (ADR-0033)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
