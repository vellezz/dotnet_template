using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding.Editors;

namespace SuperApp.Cli.Scaffolding.Plans;

/// <summary>Steps shared by the plans: templates, solutions, architecture tests, the EventId register, directories.</summary>
internal static class CommonSteps
{
    /// <summary>Path of the architecture tests project.</summary>
    public const string ArchitectureTests = "tests/SuperApp.ArchitectureTests/SuperApp.ArchitectureTests.csproj";

    /// <summary>Installs a template of the repository (it may have changed since the last run) and generates projects from it.</summary>
    /// <param name="template">Template directory under <c>src/Tools/SuperApp.Cli/Templates</c>, e.g. <c>superapp-service</c>.</param>
    /// <param name="target">Directory the template creates; the step is skipped when it exists.</param>
    /// <param name="arguments">Arguments of <c>dotnet new</c> after the template name.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep Template(string template, string target, params string[] arguments) =>
        new(target, $"dotnet new {template} {string.Join(' ', arguments)}", context =>
        {
            if (Directory.Exists(context.Files.FullPath(target)))
            {
                return [new StepResult(StepStatus.Unchanged, target, "already exists; template not run")];
            }

            // The template is installed from this repository; registrations of the same template from other clones (other paths) are
            // removed first, so dotnet new never generates code from an older copy of the template.
            var source = Path.GetFullPath(context.Files.FullPath($"{RepositoryFiles.TemplatesDirectory}/{template}")).TrimEnd('/', '\\');
            var results = new List<StepResult>();
            var installed = context.Runner.Capture("new", "uninstall").Split('\n').Select(line => line.Trim().TrimEnd('/', '\\'));
            foreach (var stale in installed.Where(line => Path.IsPathRooted(line) && Path.GetFileName(line) == template
                && !string.Equals(Path.GetFullPath(line), source, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                results.Add(context.Runner.Dotnet("new", "uninstall", stale));
            }

            results.Add(context.Runner.Dotnet("new", "install", source, "--force"));
            results.Add(context.Runner.Dotnet(["new", template, .. arguments]));
            return results;
        });

    /// <summary>Adds the projects of a directory to <c>SuperApp.slnx</c> and regenerates <c>SuperApp.sln</c>.</summary>
    /// <param name="directories">Directories whose <c>.csproj</c> files are added.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep AddToSolutions(params string[] directories) =>
        new(SolutionFiles.Slnx, $"add the projects of {string.Join(", ", directories)}; regenerate {SolutionFiles.Sln}", context =>
        {
            var projects = directories.SelectMany(directory => context.Files.Files(directory, "*.csproj")).ToList();
            return Solutions(context, content => SlnxEditor.Add(content, projects));
        });

    /// <summary>Removes the projects of a directory from <c>SuperApp.slnx</c> and regenerates <c>SuperApp.sln</c>.</summary>
    /// <param name="directories">Directories whose projects are removed.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep RemoveFromSolutions(params string[] directories) =>
        new(SolutionFiles.Slnx, $"remove the projects of {string.Join(", ", directories)}; regenerate {SolutionFiles.Sln}", context =>
        {
            var projects = SolutionFiles.ReadSlnx(context.Files.ReadOrEmpty(SolutionFiles.Slnx))
                .Select(entry => entry.ProjectPath)
                .Where(path => directories.Any(directory => path.StartsWith(directory + "/", StringComparison.Ordinal)))
                .ToList();
            return Solutions(context, content => SlnxEditor.Remove(content, projects));
        });

    /// <summary>Allocates a range of the EventId register for a new component.</summary>
    /// <param name="component">Text of the component column.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep AllocateEventIds(string component) =>
        new(EventIdRegister.Path, $"allocate 1000 event IDs to {component}", context =>
            [context.Update(EventIdRegister.Path, $"range for {component}", content => EventIdRegisterEditor.Allocate(content, component, out _))]);

    /// <summary>Gives the range of a removed component back to the EventId register.</summary>
    /// <param name="component">Text of the component column.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep ReleaseEventIds(string component) =>
        new(EventIdRegister.Path, $"release the event IDs of {component}", context =>
            [context.Update(EventIdRegister.Path, $"range of {component} released", content => EventIdRegisterEditor.Release(content, component))]);

    /// <summary>Adds project references to the architecture tests, so their rules cover the new projects.</summary>
    /// <param name="includes">Values of <c>Include</c> relative to the tests project.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep AddArchitectureReferences(params string[] includes) =>
        new(ArchitectureTests, "reference the new projects in the architecture tests", context =>
            [context.Update(ArchitectureTests, string.Join(", ", includes.Select(Path.GetFileName)), content => includes.Aggregate(content, ProjectFileEditor.AddReference))]);

    /// <summary>Removes project references from the architecture tests.</summary>
    /// <param name="projectFiles">File names of the projects.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep RemoveArchitectureReferences(params string[] projectFiles) =>
        new(ArchitectureTests, "remove the references from the architecture tests", context =>
            [context.Update(ArchitectureTests, string.Join(", ", projectFiles), content => projectFiles.Aggregate(content, ProjectFileEditor.RemoveReference))]);

    /// <summary>Deletes directories of the repository with everything in them.</summary>
    /// <param name="directories">Directories relative to the repository root.</param>
    /// <returns>The step.</returns>
    public static ScaffoldStep DeleteDirectories(params string[] directories) =>
        new(string.Join(", ", directories), "delete the code", context => [.. directories.Select(directory => context.Delete(directory, "code"))]);

    private static IEnumerable<StepResult> Solutions(ScaffoldContext context, Func<string, string> editSlnx)
    {
        var slnx = context.Update(SolutionFiles.Slnx, "projects", editSlnx);
        var sln = context.Update(SolutionFiles.Sln, $"generated from {SolutionFiles.Slnx}",
            _ => SolutionFiles.WriteSln(SolutionFiles.ReadSlnx(context.Files.ReadOrEmpty(SolutionFiles.Slnx))));
        return [slnx, sln];
    }
}
