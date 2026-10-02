using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperApp.Analyzers;

/// <summary>
/// APP003: reports APIs whose use bypasses the domain event and unit-of-work design of ADR-0027.
/// </summary>
/// <remarks>
/// <para><b>Banned APIs and why:</b></para>
/// <list type="bullet">
///   <item><description><c>MediatR.INotification</c>, <c>MediatR.INotificationHandler&lt;TNotification&gt;</c>, <c>MediatR.IPublisher</c>:
///   domain events are a separate mechanism (<c>IDomainEvent</c>, <c>IDomainEventHandler&lt;T&gt;</c>) dispatched inside
///   <c>IUnitOfWork.SaveChangesAsync</c> in the same transaction; MediatR notifications would run outside of it.</description></item>
///   <item><description><c>MediatR.IMediator</c>: MediatR is used only for commands and queries; inject <c>ISender</c>,
///   which cannot publish notifications.</description></item>
///   <item><description>Calls to <c>SaveChanges</c> (synchronous) on any type derived from <c>DbContext</c>: changes are saved
///   only through <c>IUnitOfWork.SaveChangesAsync</c>, which dispatches domain events before saving. Declaring an override of
///   <c>SaveChanges</c> is not reported, only calling it. <c>SaveChangesAsync</c> is not checked by this rule.</description></item>
/// </list>
/// <para>
/// Types are matched by their full name (generic types by their original definition), so any reference is reported: base types,
/// parameters, fields, variables, <c>typeof</c>. Generated code is not analyzed.
/// </para>
/// <para><b>How to fix:</b> use the alternative named in the diagnostic message.</para>
/// </remarks>
/// <example>
/// <code>
/// // APP003
/// public sealed record CategoryChanged(Guid Id) : INotification;
/// internal sealed class Handler(IMediator mediator, KnowledgeWriteDbContext db) { /* ... */ db.SaveChanges(); }
///
/// // correct
/// public sealed record CategoryChanged(CategoryId CategoryId) : IDomainEvent;
/// // the handler only changes aggregates; the transaction behavior calls IUnitOfWork.SaveChangesAsync and commits
/// internal sealed class RenameCategoryHandler(ICategoryRepository categories) { /* ... */ return category.Rename(command.Name); }
/// </code>
/// </example>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BannedApiAnalyzer : DiagnosticAnalyzer
{
    private const string DomainEventsReason = "domain events are handled by IDomainEventHandler<T> (ADR-0027)";

    // Full name of the banned type (as ToDisplayString prints its original definition) -> reason shown as {1} in the message.
    private static readonly Dictionary<string, string> BannedTypes = new()
    {
        ["MediatR.INotification"] = DomainEventsReason,
        ["MediatR.INotificationHandler<TNotification>"] = DomainEventsReason,
        ["MediatR.IPublisher"] = DomainEventsReason,
        ["MediatR.IMediator"] = "send commands and queries through ISender (ADR-0027)",
    };

    private const string DbContextType = "Microsoft.EntityFrameworkCore.DbContext";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.BannedApi);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;

        switch (symbol)
        {
            case INamedTypeSymbol type
                when BannedTypes.TryGetValue(type.OriginalDefinition.ToDisplayString(), out var reason):
                Report(context, name, type.OriginalDefinition.ToDisplayString(), reason);
                break;

            // Calls to the synchronous SaveChanges of any DbContext; an override declaration of SaveChanges is allowed.
            case IMethodSymbol { Name: "SaveChanges" } method
                when method.ContainingType.InheritsFrom(DbContextType)
                     && name.Parent is not MethodDeclarationSyntax:
                Report(context, name, "DbContext.SaveChanges", "save changes only through IUnitOfWork.SaveChangesAsync (ADR-0027)");
                break;
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, string api, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.BannedApi, node.GetLocation(), api, reason));
}
