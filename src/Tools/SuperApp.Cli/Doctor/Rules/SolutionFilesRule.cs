using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>
/// Every project of <c>src</c> and <c>tests</c> is in <c>SuperApp.slnx</c>, and <c>SuperApp.sln</c> lists the same projects in the same
/// solution folders.
/// </summary>
/// <remarks>
/// A project missing from the solution is not built or tested by CI and is invisible in the IDE. <c>SuperApp.sln</c> is generated from
/// <c>SuperApp.slnx</c>, so <c>--fix</c> regenerates it; a project missing from <c>SuperApp.slnx</c> must be added by hand (or by
/// <c>dotnet superapp add …</c>), because only a person knows whether it belongs to the solution.
/// </remarks>
internal sealed class SolutionFilesRule : IFixableDoctorRule
{
    /// <inheritdoc />
    public string Id => "solution-files";

    /// <inheritdoc />
    public string Description => "Every project is in SuperApp.slnx; SuperApp.sln has the same projects in the same folders.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        var files = model.Files;
        var slnx = SolutionFiles.ReadSlnx(files.ReadOrEmpty(SolutionFiles.Slnx));
        var listed = slnx.Select(entry => entry.ProjectPath).ToHashSet(StringComparer.Ordinal);

        foreach (var project in files.Files("src", "*.csproj").Concat(files.Files("tests", "*.csproj")))
        {
            if (!listed.Contains(project))
            {
                yield return new Finding(Id, Severity.Error, $"Project {project} is not in {SolutionFiles.Slnx}.", SolutionFiles.Slnx,
                    Fix: $"dotnet sln {SolutionFiles.Slnx} add {project}, then dotnet superapp doctor --fix");
            }
        }

        foreach (var entry in slnx.Where(entry => !files.Exists(entry.ProjectPath)))
        {
            yield return new Finding(Id, Severity.Error, $"{SolutionFiles.Slnx} lists {entry.ProjectPath}, which does not exist.", SolutionFiles.Slnx,
                Fix: $"dotnet sln {SolutionFiles.Slnx} remove {entry.ProjectPath}, then dotnet superapp doctor --fix");
        }

        if (!files.Exists(SolutionFiles.Sln))
        {
            yield return new Finding(Id, Severity.Error, $"{SolutionFiles.Sln} is missing.", Fix: "dotnet superapp doctor --fix");
            yield break;
        }

        var expected = Describe(slnx);
        var actual = Describe(SolutionFiles.ReadSln(files.ReadOrEmpty(SolutionFiles.Sln)));
        foreach (var difference in expected.Except(actual, StringComparer.Ordinal))
        {
            yield return new Finding(Id, Severity.Error, $"{SolutionFiles.Sln} is missing or misplaces {difference}.", SolutionFiles.Sln, Fix: "dotnet superapp doctor --fix");
        }

        foreach (var difference in actual.Except(expected, StringComparer.Ordinal))
        {
            yield return new Finding(Id, Severity.Error, $"{SolutionFiles.Sln} has {difference}, which is not in {SolutionFiles.Slnx}.", SolutionFiles.Sln, Fix: "dotnet superapp doctor --fix");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Fix(RepositoryModel model)
    {
        var files = model.Files;
        var content = SolutionFiles.WriteSln(SolutionFiles.ReadSlnx(files.ReadOrEmpty(SolutionFiles.Slnx)));
        if (files.ReadOrEmpty(SolutionFiles.Sln) == content)
        {
            return [];
        }

        File.WriteAllText(files.FullPath(SolutionFiles.Sln), content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return [SolutionFiles.Sln];
    }

    private static List<string> Describe(IEnumerable<SolutionEntry> entries) =>
        [.. entries.Select(entry => $"{entry.ProjectPath} (folder /{entry.Folder})")];
}
