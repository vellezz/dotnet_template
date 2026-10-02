using System.Text.Json.Nodes;
using SuperApp.Cli.Contracts;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>Breaking-change rules of <c>contracts diff</c> on modified copies of the real Knowledge contract.</summary>
public sealed class OpenApiDiffTests
{
    private const string Contract = "src/Services/Knowledge/Knowledge.Api/openapi/Knowledge.Api.json";

    private static readonly RepositoryFiles Files = new(RepositoryRoot.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Tests must run inside the repository."));

    private static readonly string Baseline = Files.ReadOrEmpty(Contract);

    [Fact]
    public void Same_contract_has_no_changes() => Assert.Empty(OpenApiDiff.Compare(Contract, Baseline, Baseline));

    [Fact]
    public void Removed_response_property_is_breaking_at_every_operation_using_the_schema()
    {
        var changes = Diff(document => Schema(document, "CategoryDto")["properties"]!.AsObject().Remove("slug"));

        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "GET /v1/categories 200[] .slug", Description: "response property removed" });
    }

    [Fact]
    public void Required_request_property_is_breaking_and_optional_response_property_is_compatible()
    {
        var changes = Diff(document =>
        {
            var create = Schema(document, "CreateCategory");
            create["properties"]!["color"] = new JsonObject { ["type"] = "string" };
            create["required"]!.AsArray().Add("color");
            Schema(document, "CategoryDto")["properties"]!["color"] = new JsonObject { ["type"] = "string", ["nullable"] = true };
        });

        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "POST /v1/categories body .color", Description: "required request property added" });
        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Compatible, Location: "GET /v1/categories 200[] .color" });
        Assert.DoesNotContain(changes, change => change.Kind == ContractChangeKind.Breaking && change.Location.StartsWith("GET", StringComparison.Ordinal));
    }

    [Fact]
    public void Removed_enum_value_breaks_requests_and_warns_on_responses()
    {
        var changes = Diff(document => Schema(document, "MaterialType")["enum"]!.AsArray().RemoveAt(2));

        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "POST /v1/materials body .type" });
        Assert.Contains(changes, change => change.Kind == ContractChangeKind.Warning && change.Location.StartsWith("GET /v1/materials/{materialId} 200", StringComparison.Ordinal));
    }

    [Fact]
    public void Removed_operation_and_changed_type_are_breaking()
    {
        var changes = Diff(document =>
        {
            document["paths"]!["/v1/categories"]!.AsObject().Remove("post");
            Schema(document, "CategoryDto")["properties"]!["id"]!.AsObject().Remove("format");
        });

        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "POST /v1/categories", Description: "operation removed" });
        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "GET /v1/categories 200[] .id", Description: "type changed from string (uuid) to string" });
        Assert.Equal(ContractChangeKind.Breaking, changes[0].Kind);
    }

    [Fact]
    public void New_variant_of_a_polymorphic_response_is_a_warning()
    {
        var changes = Diff(document => Schema(document, "ContentBlockDto")["anyOf"]!.AsArray()
            .Add(new JsonObject { ["$ref"] = "#/components/schemas/ImageBlockDto" }));

        Assert.Contains(changes, change => change.Kind == ContractChangeKind.Warning && change.Description.StartsWith("variant ImageBlockDto added", StringComparison.Ordinal));
        Assert.DoesNotContain(changes, change => change.Kind == ContractChangeKind.Breaking && change.Location.StartsWith("GET", StringComparison.Ordinal));
    }

    [Fact]
    public void New_required_parameter_is_breaking_and_removed_contract_too()
    {
        var changes = Diff(document =>
        {
            var operation = document["paths"]!["/v1/categories"]!["get"]!.AsObject();
            operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "locale", ["in"] = "query", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
        });
        var removed = new ContractBaseline(new Dictionary<string, string> { [Contract] = Baseline }, "test")
            .CompareWith(new ContractBaseline(new Dictionary<string, string>(), "test"));

        Assert.Contains(changes, change => change is { Kind: ContractChangeKind.Breaking, Location: "GET /v1/categories query:locale", Description: "required parameter added" });
        Assert.Equal("contract removed", Assert.Single(removed).Description);
    }

    private static List<ContractChange> Diff(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(Baseline)!.AsObject();
        change(document);
        return [.. OpenApiDiff.Compare(Contract, Baseline, document.ToJsonString())];
    }

    private static JsonObject Schema(JsonObject document, string name) => document["components"]!["schemas"]![name]!.AsObject();
}
