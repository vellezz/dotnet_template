using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SuperApp.Analyzers;

/// <summary>
/// APP001: the value returned by a method of type <c>Result</c> / <c>Result&lt;T&gt;</c> (from <c>SuperApp.Framework.Domain.Results</c>)
/// must not be silently discarded (ADR-0015).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why:</b> business rule violations are not exceptions in this system; aggregate methods, factories and command handlers
/// return a <c>Result</c> carrying an <c>Error</c>. A call whose result is dropped therefore swallows a failure silently: for example
/// an aggregate may refuse a state change and the handler would still report success.
/// </para>
/// <para>
/// <b>What is reported:</b> an expression statement consisting of a method invocation (optionally awaited) whose type is
/// <c>Result</c> or a type derived from it (<c>Result&lt;T&gt;</c>). Assigning the value, returning it, passing it on or
/// discarding it explicitly with <c>_ = ...</c> is not reported. Generated code is not analyzed.
/// </para>
/// <para>
/// <b>How to fix:</b> check the result and propagate the error (<c>if (result.IsFailure) return result.Error;</c>) or return it
/// directly. Use <c>_ = ...</c> only when ignoring the outcome is really intended, so the intent is visible in review.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // APP001: the failure is lost
/// category.Rename(command.Name);
///
/// // correct: the failure reaches the caller
/// var result = category.Rename(command.Name);
/// if (result.IsFailure)
/// {
///     return result.Error;
/// }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultUsageAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.IgnoredResult);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeExpressionStatement, OperationKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(OperationAnalysisContext context)
    {
        var statement = (IExpressionStatementOperation)context.Operation;
        var expression = statement.Operation;

        // "await SaveAsync()" produces a Result only after awaiting: check the awaited type, then inspect the awaited invocation.
        if (expression is IAwaitOperation awaitOperation)
        {
            expression = awaitOperation.Operation;
            if (!awaitOperation.Type.IsResultType())
            {
                return;
            }
        }
        else if (!expression.Type.IsResultType())
        {
            return;
        }

        // Only discarded invocations are reported; other expression statements of type Result are not method results.
        if (expression is IInvocationOperation)
        {
            var type = statement.Operation.Type!;
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.IgnoredResult,
                statement.Syntax.GetLocation(),
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
