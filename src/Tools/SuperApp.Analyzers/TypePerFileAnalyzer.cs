using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperApp.Analyzers;

/// <summary>
/// APP004: one top-level type per file. APP005: the file is named after that type (ADR-0032).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why:</b> a type can then be found by its file name, and a change to one type shows up as a change to one file in review.
/// </para>
/// <para><b>What is reported:</b></para>
/// <list type="bullet">
///   <item><description>APP004 on every top-level type (class, record, struct, interface, enum, delegate) after the first one in
///   the file. Nested types belong to their containing type and are allowed.</description></item>
///   <item><description>APP005 on the first type when the file name, cut at the first <c>.</c> and at the first <c>{</c>, differs
///   from the type name (case-sensitive). Allowed names for <c>Order</c>: <c>Order.cs</c>; for generic <c>Result&lt;T&gt;</c>:
///   <c>Result{T}.cs</c> or <c>Result.cs</c>; for another part of a <c>partial</c> type: <c>Order.Log.cs</c>.</description></item>
/// </list>
/// <para>
/// Files without types (e.g. <c>Program.cs</c> with top-level statements) and generated code (EF migrations, source generator
/// output) are not analyzed.
/// </para>
/// <para><b>How to fix:</b> move each extra type to its own file named after it, or rename the file.</para>
/// </remarks>
/// <example>
/// <code>
/// // Ids.cs
/// public readonly record struct MaterialId(Guid Value);  // APP005: must be in MaterialId.cs
/// public readonly record struct CategoryId(Guid Value);  // APP004: move to CategoryId.cs
///
/// // correct: MaterialId.cs and CategoryId.cs, one type each
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypePerFileAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MultipleTypesInFile, Descriptors.FileNameMismatch);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        // Only top-level types: descend into the compilation unit and namespaces, never into type bodies (nested types are allowed).
        var types = context.Tree.GetRoot(context.CancellationToken)
            .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => (Node: node, Name: Identifier(node)))
            .ToList();

        if (types.Count == 0)
        {
            return;
        }

        foreach (var (node, name) in types.Skip(1))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.MultipleTypesInFile, Identifier(node).GetLocation(), name.Text));
        }

        var fileName = Path.GetFileNameWithoutExtension(context.Tree.FilePath);
        // "Order.Log" -> "Order" (partial part), "Result{T}" -> "Result" (generic type).
        var baseName = fileName.Split('.')[0].Split('{')[0];
        var first = types[0].Name;
        if (!string.IsNullOrEmpty(fileName) && !string.Equals(baseName, first.Text, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.FileNameMismatch, first.GetLocation(), first.Text, fileName));
        }
    }

    private static SyntaxToken Identifier(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier,
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };
}
