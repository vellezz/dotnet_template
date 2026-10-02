using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace SuperApp.Analyzers.Tests;

/// <summary>
/// Tests of the <c>SuperApp.Analyzers</c> rules (APP001-APP006): for every rule, violating code reports the diagnostic
/// and the correct alternative in the same source does not.
/// </summary>
/// <remarks>
/// <para>
/// Tests use <c>Microsoft.CodeAnalysis.Testing</c>: the expected diagnostics are marked inline in the source as
/// <c>{|APP001:code|}</c>; any unmarked diagnostic, or a marked one that is not reported, fails the test. Unmarked lines are
/// therefore the "correct code" half of each test.
/// </para>
/// <para>
/// The sources are compiled against .NET reference assemblies only, so the framework and library types the analyzers look
/// for are replaced by the minimal stubs in <c>Stubs</c>. When adding a rule, add a test here with both a violation and the allowed form.
/// </para>
/// </remarks>
public sealed class AnalyzerTests
{
    // Minimal stand-ins for framework and library types; the analyzers recognize them by full name, so the namespaces must match.
    private const string Stubs = """
        namespace SuperApp.Framework.Domain.Results
        {
            public class Result { public static Result Ok() => new Result(); }
        }
        namespace SuperApp.Framework.Domain.ValueObjects
        {
            public interface ISingleValueObject<TSelf, TValue> { }
        }
        namespace MediatR
        {
            public interface INotification { }
            public interface INotificationHandler<TNotification> { }
            public interface IPublisher { }
            public interface IMediator { }
            public interface ISender { }
        }
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public virtual int SaveChanges() => 0;
                public virtual System.Threading.Tasks.Task<int> SaveChangesAsync() => System.Threading.Tasks.Task.FromResult(0);
            }
        }
        """;

    [Fact]
    public Task APP001_reports_ignored_result() => VerifyAsync<ResultUsageAnalyzer>("""
        using SuperApp.Framework.Domain.Results;
        class Order
        {
            Result Ship() => Result.Ok();
            void Handle()
            {
                {|APP001:Ship();|}
                var result = Ship();
                _ = Ship();
            }
        }
        """);

    [Fact]
    public Task APP002_reports_default_and_parameterless_creation() => VerifyAsync<SingleValueObjectCreationAnalyzer>("""
        using SuperApp.Framework.Domain.Results;
        using SuperApp.Framework.Domain.ValueObjects;
        readonly struct OrderId : ISingleValueObject<OrderId, System.Guid>
        {
            private OrderId(System.Guid value) { }
            public static OrderId FromTrusted(System.Guid value) => new OrderId(value);
        }
        class Usage
        {
            void Create()
            {
                var a = {|APP002:default(OrderId)|};
                var b = {|APP002:new OrderId()|};
                var c = OrderId.FromTrusted(System.Guid.NewGuid());
            }
        }
        """);

    [Fact]
    public Task APP003_reports_banned_mediator_types_and_sync_save() => VerifyAsync<BannedApiAnalyzer>("""
        using MediatR;
        using Microsoft.EntityFrameworkCore;
        class Event : {|APP003:INotification|} { }
        class Handler({|APP003:IPublisher|} publisher, {|APP003:IMediator|} mediator, ISender sender) { }
        class Context : DbContext
        {
            void Save()
            {
                this.{|APP003:SaveChanges|}();
                _ = SaveChangesAsync();
            }
        }
        """);

    [Fact]
    public Task APP004_reports_every_type_after_the_first() => VerifyFilesAsync<TypePerFileAnalyzer>(
        ("/0/Order.cs", """
            namespace Shop;
            public sealed class Order { }
            public enum {|APP004:OrderStatus|} { New }
            public interface {|APP004:IOrderRepository|} { }
            """));

    [Fact]
    public Task APP004_allows_nested_types_and_partial_parts() => VerifyFilesAsync<TypePerFileAnalyzer>(
        ("/0/Order.cs", """
            namespace Shop;
            public sealed partial class Order { private sealed class Line { } }
            """),
        ("/0/Order.Log.cs", """
            namespace Shop;
            public sealed partial class Order { }
            """),
        ("/0/Result{T}.cs", """
            namespace Shop;
            public sealed class Result<T> { }
            """));

    [Fact]
    public Task APP005_reports_file_name_different_from_type() => VerifyFilesAsync<TypePerFileAnalyzer>(
        ("/0/Ids.cs", """
            namespace Shop;
            public readonly record struct {|APP005:OrderId|}(System.Guid Value);
            """));

    [Fact]
    public Task APP006_reports_missing_param_typeparam_and_returns() => VerifyDocumentedAsync<DocumentationCompletenessAnalyzer>("""
        namespace Shop;
        /// <summary>Order.</summary>
        /// <param name="Id">Identifier.</param>
        public sealed record {|APP006:Order|}(System.Guid Id, string Number);
        /// <summary>Repository.</summary>
        public interface {|APP006:IRepository|}<T>
        {
            /// <summary>Gets an item.</summary>
            System.Threading.Tasks.Task<T> {|APP006:GetAsync|}(System.Guid id);
            /// <summary>Saves an item.</summary>
            /// <param name="item">Item.</param>
            System.Threading.Tasks.Task SaveAsync(T item);
        }
        /// <summary>Implementation.</summary>
        /// <param name="name">Name.</param>
        public sealed class Handler(string name)
        {
            /// <inheritdoc />
            public override string ToString() => name;
            /// <summary>Returns the name.</summary>
            /// <returns>Name.</returns>
            public string Name() => name;
            internal int Hidden(int value) => value;
        }
        """);

    // One source file plus the stubs.
    private static Task VerifyAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(source);
        test.TestState.Sources.Add(Stubs);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Several files with explicit paths, for the file-name rules; no stubs needed.
    private static Task VerifyFilesAsync<TAnalyzer>(params (string Path, string Source)[] files)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        foreach (var file in files)
        {
            test.TestState.Sources.Add(file);
        }

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    // XML documentation comments are parsed only in DocumentationMode.Diagnose, which APP006 needs to read them.
    private static Task VerifyDocumentedAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(source);
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            return solution.WithProjectParseOptions(projectId, project.ParseOptions!.WithDocumentationMode(Microsoft.CodeAnalysis.DocumentationMode.Diagnose));
        });
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
