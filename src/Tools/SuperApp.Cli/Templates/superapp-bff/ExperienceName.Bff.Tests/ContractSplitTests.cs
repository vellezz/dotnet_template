using System.Text.Json;

namespace ExperienceName.Bff.Tests;

/// <summary>
/// The committed contracts keep public and internal API apart (ADR-0039): the module's client never sees an internal operation, and
/// consumers of the internal API never depend on a public one.
/// </summary>
public sealed class ContractSplitTests
{
    [Fact]
    public void Public_contract_has_no_internal_paths_and_internal_contract_only_internal_ones()
    {
        var publicPaths = Paths("ExperienceName.Bff_public.json");
        var internalPaths = Paths("ExperienceName.Bff_internal.json");

        Assert.All(publicPaths, path => Assert.StartsWith("/v", path, StringComparison.Ordinal));
        Assert.All(internalPaths, path => Assert.StartsWith("/internal/v", path, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_operation_requires_a_bearer_token_and_has_an_operation_id()
    {
        foreach (var file in new[] { "ExperienceName.Bff_public.json", "ExperienceName.Bff_internal.json" })
        {
            using var document = Load(file);
            foreach (var operation in document.RootElement.GetProperty("paths").EnumerateObject().SelectMany(path => path.Value.EnumerateObject()))
            {
                Assert.True(operation.Value.TryGetProperty("operationId", out _), $"{file}: operation without operationId");
                Assert.True(operation.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0, $"{file}: operation without security");
            }
        }
    }

    private static List<string> Paths(string file)
    {
        using var document = Load(file);
        return [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name)];
    }

    private static JsonDocument Load(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Bff", "ExperienceName.Bff", "openapi")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "src", "Bff", "ExperienceName.Bff", "openapi", file)));
    }
}
