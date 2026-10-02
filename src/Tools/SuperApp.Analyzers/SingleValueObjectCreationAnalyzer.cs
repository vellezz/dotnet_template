using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SuperApp.Analyzers;

/// <summary>
/// APP002: strongly typed IDs and single-value objects (types implementing <c>ISingleValueObject&lt;TSelf, TValue&gt;</c>, which
/// includes every <c>IStronglyTypedId&lt;TSelf, TValue&gt;</c>) may be created only through their factory methods (ADR-0023, ADR-0024).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why:</b> these types are <c>readonly record struct</c>s. C# always allows <c>default(T)</c> and the parameterless
/// <c>new T()</c> for structs, even when the only declared constructor is private. Both produce a value that never passed
/// validation (for example an empty <c>Guid</c> ID or a <see langword="null"/> string inside a value object), which breaks
/// the invariants the type promises.
/// </para>
/// <para>
/// <b>What is reported:</b> a <c>default</c> expression (both <c>default(T)</c> and the <c>default</c> literal) whose type is a
/// single-value object, and an object creation of such a type without arguments. <c>Nullable&lt;T&gt;</c> (<c>MaterialId?</c>)
/// is not affected. Generated code is not analyzed.
/// </para>
/// <para>
/// <b>How to fix:</b> use <c>Create(value)</c> for untrusted input (it returns <c>Result&lt;T&gt;</c>), <c>FromTrusted(value)</c>
/// for values already validated (e.g. read from the database), or <c>New()</c> to generate a new ID. For "no value" use a nullable type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var id = default(MaterialId);          // APP002
/// var other = new MaterialId();          // APP002
///
/// var generated = MaterialId.New();      // correct: a new identifier
/// var parsed = MaterialId.Create(guid);  // correct: validated, returns Result&lt;MaterialId&gt;
/// MaterialId? none = null;               // correct: absence of a value
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleValueObjectCreationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.UninitializedValueObject);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeDefault, OperationKind.DefaultValue);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeDefault(OperationAnalysisContext context)
    {
        var type = context.Operation.Type;
        if (type.IsSingleValueObject())
        {
            Report(context, type!);
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Arguments.IsEmpty && creation.Type.IsSingleValueObject())
        {
            Report(context, creation.Type!);
        }
    }

    private static void Report(OperationAnalysisContext context, ITypeSymbol type) =>
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.UninitializedValueObject,
            context.Operation.Syntax.GetLocation(),
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
}
