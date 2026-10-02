using System.Reflection;
using Architecture = ArchUnitNET.Domain.Architecture;
using Assembly = System.Reflection.Assembly;
using ArchUnitNET.Loader;

namespace SuperApp.ArchitectureTests;

/// <summary>
/// The production assemblies of the solution, loaded once for all architecture rules, and the services discovered in them.
/// </summary>
/// <remarks>
/// <para>
/// Assemblies come from the test output directory, where the build copies everything referenced by the csproj. A service is
/// recognized by convention: an assembly named <c>{Service}.Domain</c> outside the <c>SuperApp.</c> prefix (ADR-0025).
/// Production assemblies are the <c>SuperApp.*</c> ones (except the analyzers, which are not runtime code) and all assemblies whose
/// first name segment has a matching <c>{Service}.Domain.dll</c>; test assemblies and third-party libraries are excluded.
/// </para>
/// <para>
/// Pitfall: a service whose Api and Worker are not referenced from <c>SuperApp.ArchitectureTests.csproj</c> is not in the output
/// directory and therefore silently not checked.
/// </para>
/// </remarks>
internal static class Solution
{
    /// <summary>All production assemblies (framework, gateway, migrator, every layer of every service).</summary>
    public static readonly IReadOnlyList<Assembly> Assemblies = LoadProductionAssemblies();

    /// <summary>Service names derived from <c>{Service}.Domain</c> assemblies, sorted, e.g. <c>Knowledge</c>, <c>SleepDiary</c>.</summary>
    public static readonly IReadOnlyList<string> Services = Assemblies
        .Select(assembly => assembly.GetName().Name!)
        .Where(name => name.EndsWith(".Domain", StringComparison.Ordinal) && !name.StartsWith("SuperApp.", StringComparison.Ordinal))
        .Select(name => name[..^".Domain".Length])
        .OrderBy(name => name)
        .ToList();

    /// <summary>BFF assemblies of experiences (<c>{Experience}.Bff</c>, ADR-0038), e.g. <c>Example.Bff</c>.</summary>
    public static readonly IReadOnlyList<Assembly> Bffs = Assemblies
        .Where(assembly => assembly.GetName().Name!.EndsWith(".Bff", StringComparison.Ordinal))
        .ToList();

    /// <summary>The ArchUnitNET model of <see cref="Assemblies"/>, built once because loading is expensive.</summary>
    public static readonly Architecture Architecture = new ArchLoader().LoadAssemblies([.. Assemblies]).Build();

    /// <summary>Returns the assemblies of one layer across all services.</summary>
    /// <param name="layer">Layer suffix of the assembly name, e.g. <c>Application</c> or <c>Infrastructure</c>.</param>
    /// <returns>The <c>{Service}.{layer}</c> assemblies of every discovered service that has such a project.</returns>
    public static IEnumerable<Assembly> Layer(string layer) =>
        Assemblies.Where(assembly => Services.Any(service => assembly.GetName().Name == $"{service}.{layer}"));

    private static List<Assembly> LoadProductionAssemblies()
    {
        var directory = AppContext.BaseDirectory;
        return Directory.GetFiles(directory, "*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(IsProductionAssembly)
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .ToList();
    }

    private static bool IsProductionAssembly(string name) =>
        !name.EndsWith("Tests", StringComparison.Ordinal)
        && (name.StartsWith("SuperApp.", StringComparison.Ordinal)
            || name.EndsWith(".Bff", StringComparison.Ordinal)
            || File.Exists(Path.Combine(AppContext.BaseDirectory, $"{name.Split('.')[0]}.Domain.dll")))
        && name != "SuperApp.Analyzers";
}
