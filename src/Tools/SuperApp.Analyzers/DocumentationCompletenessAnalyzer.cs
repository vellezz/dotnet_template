using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperApp.Analyzers;

/// <summary>
/// APP006: the XML documentation of the public API is complete, not just present (ADR-0033).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why:</b> the compiler (CS1591) only requires a <c>summary</c>. Parameters, type parameters and return values are exactly
/// what a newcomer needs to use an API correctly, and controller docs end up in the OpenAPI contract, so the rest is checked here.
/// </para>
/// <para><b>What is required</b> on externally visible symbols (public, protected or protected internal, and so are all containing types):</para>
/// <list type="bullet">
///   <item><description>types: <c>typeparam</c> for every type parameter; <c>param</c> for every parameter of a positional
///   record or primary constructor; for delegates also <c>param</c> and <c>returns</c> of the signature;</description></item>
///   <item><description>methods, constructors, operators and conversions: <c>param</c> for every parameter, <c>typeparam</c> for
///   every type parameter and <c>returns</c> unless the method returns <c>void</c>, non-generic <c>Task</c> or <c>ValueTask</c>
///   (constructors never need <c>returns</c>);</description></item>
///   <item><description>indexers: <c>param</c> for every parameter;</description></item>
///   <item><description>types, when the <c>.editorconfig</c> option <c>app_documentation_require_remarks = true</c> applies to the
///   file (enabled for <c>src/Framework</c>): also <c>remarks</c>.</description></item>
/// </list>
/// <para>
/// Symbols without any documentation are left to CS1591; a comment containing <c>inheritdoc</c> counts as complete; malformed
/// XML is left to CS1570. Implicitly declared symbols and generated code are skipped. Test projects disable CS1591 and APP006
/// in <c>Directory.Build.props</c>.
/// </para>
/// <para><b>How to fix:</b> add the tags listed in the message, or <c>&lt;inheritdoc /&gt;</c> for an implementation that adds nothing.</para>
/// </remarks>
/// <example>
/// <code>
/// /// &lt;summary&gt;Loads a category.&lt;/summary&gt;
/// Task&lt;Category?&gt; GetAsync(CategoryId id, CancellationToken cancellationToken);   // APP006: param id, param cancellationToken, returns
///
/// /// &lt;summary&gt;Loads a category for modification.&lt;/summary&gt;
/// /// &lt;param name="id"&gt;Identifier of the category.&lt;/param&gt;
/// /// &lt;param name="cancellationToken"&gt;Cancels the database query.&lt;/param&gt;
/// /// &lt;returns&gt;The category, or &lt;see langword="null"/&gt; when it does not exist.&lt;/returns&gt;
/// Task&lt;Category?&gt; GetAsync(CategoryId id, CancellationToken cancellationToken);   // correct
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationCompletenessAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.IncompleteDocumentation);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(
            AnalyzeSymbol,
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Property);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;
        if (symbol.IsImplicitlyDeclared || !IsExternallyVisible(symbol) || symbol.Locations.FirstOrDefault() is not { IsInSource: true } location)
        {
            return;
        }

        var xml = symbol.GetDocumentationCommentXml(cancellationToken: context.CancellationToken);
        // No documentation at all is CS1591's job; inheritdoc takes the documentation from the base member.
        if (string.IsNullOrWhiteSpace(xml) || xml!.Contains("<inheritdoc"))
        {
            return;
        }

        XElement doc;
        try
        {
            doc = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return; // Malformed XML is reported by the compiler (CS1570).
        }

        var missing = new List<string>();
        switch (symbol)
        {
            case INamedTypeSymbol type:
                if (RequiresRemarks(context, location) && !doc.Elements("remarks").Any())
                {
                    missing.Add("<remarks>");
                }

                missing.AddRange(MissingTags(doc, "typeparam", type.TypeParameters.Select(p => p.Name)));
                if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod is { } invoke)
                {
                    missing.AddRange(MissingTags(doc, "param", invoke.Parameters.Select(p => p.Name)));
                    AddReturns(doc, invoke, missing);
                }
                else if (PrimaryConstructor(type) is { } primary)
                {
                    missing.AddRange(MissingTags(doc, "param", primary.Parameters.Select(p => p.Name)));
                }

                break;

            case IMethodSymbol method when IsDocumentedMethod(method):
                missing.AddRange(MissingTags(doc, "param", method.Parameters.Select(p => p.Name)));
                missing.AddRange(MissingTags(doc, "typeparam", method.TypeParameters.Select(p => p.Name)));
                AddReturns(doc, method, missing);
                break;

            case IPropertySymbol { IsIndexer: true } indexer:
                missing.AddRange(MissingTags(doc, "param", indexer.Parameters.Select(p => p.Name)));
                break;
        }

        if (missing.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.IncompleteDocumentation, location, symbol.Name, string.Join(", ", missing)));
        }
    }

    private static bool RequiresRemarks(SymbolAnalysisContext context, Location location) =>
        location.SourceTree is { } tree
        && context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue("app_documentation_require_remarks", out var value)
        && string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);

    private static bool IsDocumentedMethod(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion
        && !IsPrimaryConstructor(method);

    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.FirstOrDefault(IsPrimaryConstructor);

    // A primary constructor is declared by the type declaration itself; its parameters are documented on the type.
    private static bool IsPrimaryConstructor(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Constructor
        && method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax);

    private static void AddReturns(XElement doc, IMethodSymbol method, List<string> missing)
    {
        var type = method.ReturnType;
        var returnsNothing = method.ReturnsVoid
            || (type is INamedTypeSymbol { IsGenericType: false } named
                && named.ToDisplayString() is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask");
        if (!returnsNothing && method.MethodKind != MethodKind.Constructor && !doc.Elements("returns").Any())
        {
            missing.Add("<returns>");
        }
    }

    private static IEnumerable<string> MissingTags(XElement doc, string tag, IEnumerable<string> names)
    {
        var documented = new HashSet<string>(doc.Elements(tag).Select(e => (string?)e.Attribute("name") ?? string.Empty));
        return names.Where(name => !documented.Contains(name)).Select(name => $"<{tag} name=\"{name}\">");
    }

    // A public member of an internal type is not public API: every containing type must be visible as well.
    private static bool IsExternallyVisible(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }
}
