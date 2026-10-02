using System.Text.Json;
using System.Text.RegularExpressions;
using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>The version of the tool in its project equals the version pinned in the tool manifest.</summary>
/// <remarks>
/// The tool is restored from <c>tools/packages</c> by the exact version in <c>.config/dotnet-tools.json</c>. NuGet caches packages by
/// version, so a changed tool with an unchanged version is never picked up, and a manifest pointing to another version than the project
/// builds fails <c>dotnet tool restore</c>. Every change of the tool raises the version in both files (ADR-0046).
/// </remarks>
internal sealed partial class ToolVersionRule : IDoctorRule
{
    private const string Project = "src/Tools/SuperApp.Cli/SuperApp.Cli.csproj";
    private const string Manifest = ".config/dotnet-tools.json";

    /// <inheritdoc />
    public string Id => "tool-version";

    /// <inheritdoc />
    public string Description => "SuperApp.Cli has the same version in its project and in .config/dotnet-tools.json.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        var projectVersion = VersionElement().Match(files.ReadOrEmpty(Project)) is { Success: true } match ? match.Groups[1].Value : null;
        string? manifestVersion = null;
        var manifest = files.ReadOrEmpty(Manifest);
        if (manifest.Length > 0)
        {
            using var document = JsonDocument.Parse(manifest);
            if (document.RootElement.TryGetProperty("tools", out var tools) && tools.TryGetProperty("superapp.cli", out var tool)
                && tool.TryGetProperty("version", out var version))
            {
                manifestVersion = version.GetString();
            }
        }

        if (projectVersion is null || manifestVersion is null)
        {
            yield return new Finding(Id, Severity.Error, $"The version of SuperApp.Cli is missing in {(projectVersion is null ? Project : Manifest)}.", projectVersion is null ? Project : Manifest,
                Fix: "Set <Version> in the project and the same version of superapp.cli in the manifest.");
        }
        else if (projectVersion != manifestVersion)
        {
            yield return new Finding(Id, Severity.Error, $"SuperApp.Cli is {projectVersion} in {Project} but {manifestVersion} in {Manifest}.", Manifest,
                Fix: "Use the same version in both files, then run tools/bootstrap.ps1 (or .sh).");
        }
    }

    [GeneratedRegex("<Version>([^<]+)</Version>")]
    private static partial Regex VersionElement();
}
