using System.CommandLine;
using System.Text.Json;
using SuperApp.Cli.Contracts;
using SuperApp.Cli.Output;
using SuperApp.Cli.Repository;
using SuperApp.Cli.Scaffolding;

namespace SuperApp.Cli.Commands;

/// <summary><c>dotnet superapp contracts [--check]</c>: regenerates the committed OpenAPI contracts and the generated clients (ADR-0014, ADR-0019).</summary>
/// <remarks>
/// <para>
/// The contracts are produced by the build (<c>openapi/*.json</c> of every Api and BFF) and the clients by Refitter from those contracts
/// (<c>*.refitter</c> of the BFFs and of service ACLs). Their order matters: a service contract feeds a client, the client feeds the BFF
/// contract. The command builds the solution, runs Refitter for every settings file and builds again when a client changed.
/// </para>
/// <para>
/// <c>--check</c> is for CI and before a pull request: it regenerates, reports the files that were out of date, restores them and exits
/// with <see cref="ExitCodes.FindingsFound"/> when any was. A contract that changed without its diff in the review is a change the
/// consumers did not see (chapter 08).
/// </para>
/// <para>
/// <c>contracts snapshot</c> and <c>contracts diff</c> answer whether a change breaks consumers: the snapshot stores the contracts before the
/// work, the diff compares them (or the contracts of a git revision, <c>--git origin/main</c>) with the current ones through
/// <see cref="OpenApiDiff"/> and exits with <see cref="ExitCodes.FindingsFound"/> when any change is breaking.
/// </para>
/// </remarks>
internal static class ContractsCommand
{
    /// <summary>Creates the command.</summary>
    /// <param name="common">Options shared by all commands.</param>
    /// <returns>The <c>contracts</c> command.</returns>
    public static Command Create(CommonOptions common)
    {
        var check = new Option<bool>("--check") { Description = "Only check that the committed contracts and clients are up to date; restore every file afterwards." };
        var command = new Command("contracts", "Regenerate the OpenAPI contracts of services and BFFs and the Refitter clients built from them.")
        {
            check,
            Snapshot(common),
            Diff(common),
        };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var files = new RepositoryFiles(root);
            var runner = new ProcessRunner(root);
            var tracked = Tracked(files);
            var before = tracked.ToDictionary(path => path, path => files.ReadOrEmpty(path), StringComparer.Ordinal);
            try
            {
                runner.Dotnet("build", SolutionFiles.Slnx, "--nologo", "-v", "q");
                var clients = Clients(files);
                var beforeClients = clients.ToDictionary(path => path, files.ReadOrEmpty, StringComparer.Ordinal);
                foreach (var settings in files.Files("src", "*.refitter"))
                {
                    runner.Dotnet("refitter", "--settings-file", settings, "--no-logging");
                }

                if (clients.Any(path => files.ReadOrEmpty(path) != beforeClients[path]))
                {
                    runner.Dotnet("build", SolutionFiles.Slnx, "--nologo", "-v", "q");
                }
            }
            catch (ScaffoldException exception)
            {
                output.Error(exception.Message);
                return ExitCodes.Failed;
            }

            var changed = Tracked(files).Where(path => !before.TryGetValue(path, out var old) || old != files.ReadOrEmpty(path)).ToList();
            var isCheck = parseResult.GetValue(check);
            if (isCheck)
            {
                foreach (var (path, content) in before)
                {
                    if (files.ReadOrEmpty(path) != content)
                    {
                        File.WriteAllText(files.FullPath(path), content);
                    }
                }
            }

            Write(output, changed, isCheck);
            return isCheck && changed.Count > 0 ? ExitCodes.FindingsFound : ExitCodes.Success;
        });
        return command;
    }

    private static Command Snapshot(CommonOptions common)
    {
        var directory = new Option<string>("--output") { Description = $"Snapshot directory (default: {ContractBaseline.DefaultDirectory})." };
        var command = new Command("snapshot", "Store the current contracts as the baseline for a later 'contracts diff' (works without git).") { directory };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var files = new RepositoryFiles(root);
            var target = files.FullPath(parseResult.GetValue(directory) ?? ContractBaseline.DefaultDirectory);
            var current = ContractBaseline.Current(files);
            current.WriteTo(target);
            if (output.IsJson)
            {
                output.WriteJson(new { directory = target, contracts = current.Contracts.Keys.Order(StringComparer.Ordinal) });
            }
            else
            {
                output.Line($"Stored {current.Contracts.Count} contracts in {target}.");
            }

            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Diff(CommonOptions common)
    {
        var directory = new Option<string>("--baseline") { Description = $"Snapshot directory to compare with (default: {ContractBaseline.DefaultDirectory})." };
        var git = new Option<string>("--git") { Description = "Compare with the contracts committed in a git revision instead, e.g. origin/main." };
        var all = new Option<bool>("--all") { Description = "Also list compatible changes." };
        var command = new Command("diff", "Compare the current contracts with a baseline and report breaking changes for consumers (exit code 2 when any).")
        {
            directory,
            git,
            all,
        };
        command.SetAction(parseResult =>
        {
            var output = common.Output(parseResult);
            if (common.FindRoot(parseResult, output) is not { } root)
            {
                return ExitCodes.NotFound;
            }

            var files = new RepositoryFiles(root);
            ContractBaseline? baseline;
            try
            {
                baseline = parseResult.GetValue(git) is { } revision
                    ? ContractBaseline.FromGit(new ProcessRunner(root), revision)
                    : ContractBaseline.FromDirectory(files.FullPath(parseResult.GetValue(directory) ?? ContractBaseline.DefaultDirectory));
            }
            catch (ScaffoldException exception)
            {
                output.Error(exception.Message);
                return ExitCodes.Failed;
            }

            if (baseline is null)
            {
                output.Error("No baseline: run 'dotnet superapp contracts snapshot' before the change, or pass --git <revision>.");
                return ExitCodes.NotFound;
            }

            var changes = baseline.CompareWith(ContractBaseline.Current(files));
            WriteChanges(output, baseline.Source, changes, parseResult.GetValue(all));
            return changes.Any(change => change.Kind == ContractChangeKind.Breaking) ? ExitCodes.FindingsFound : ExitCodes.Success;
        });
        return command;
    }

    private static void WriteChanges(OutputWriter output, string source, IReadOnlyList<ContractChange> changes, bool all)
    {
        int Count(ContractChangeKind kind) => changes.Count(change => change.Kind == kind);
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                baseline = source,
                breaking = Count(ContractChangeKind.Breaking),
                warnings = Count(ContractChangeKind.Warning),
                compatible = Count(ContractChangeKind.Compatible),
                changes = changes.Select(change => new { kind = change.Kind.ToString().ToLowerInvariant(), change.Contract, change.Location, change.Description }),
            });
            return;
        }

        var shown = changes.Where(change => all || change.Kind != ContractChangeKind.Compatible).ToList();
        if (shown.Count > 0)
        {
            output.Table(["Kind", "Contract", "Location", "Change"],
                shown.Select(change => (IReadOnlyList<string>)[change.Kind.ToString(), change.Contract, change.Location, change.Description]));
            output.Line();
        }

        output.Line($"Compared with {source}: {Count(ContractChangeKind.Breaking)} breaking, {Count(ContractChangeKind.Warning)} warnings, {Count(ContractChangeKind.Compatible)} compatible.");
        if (Count(ContractChangeKind.Breaking) > 0)
        {
            output.Line("A breaking change needs a new version of the operation or the contract (/v{n+1}), never an in-place change (chapter 08).");
        }
    }

    // Contracts of every Api and BFF plus the clients generated from them.
    private static List<string> Tracked(RepositoryFiles files) =>
        [.. files.Files("src", "*.json").Where(path => path.Contains("/openapi/", StringComparison.Ordinal)).Concat(Clients(files))];

    private static List<string> Clients(RepositoryFiles files)
    {
        var clients = new List<string>();
        foreach (var settings in files.Files("src", "*.refitter"))
        {
            using var document = JsonDocument.Parse(files.ReadOrEmpty(settings));
            if (document.RootElement.TryGetProperty("outputFolder", out var folder) && document.RootElement.TryGetProperty("outputFilename", out var name))
            {
                var directory = Path.GetDirectoryName(settings) ?? string.Empty;
                clients.Add(files.Relative(files.FullPath(Path.Combine(directory, folder.GetString() ?? ".", name.GetString() ?? string.Empty))));
            }
        }

        return clients;
    }

    private static void Write(OutputWriter output, List<string> changed, bool check)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { check, upToDate = changed.Count == 0, changed });
            return;
        }

        if (changed.Count == 0)
        {
            output.Line("Contracts and generated clients are up to date.");
            return;
        }

        output.Line(check ? "Out of date (restored; run dotnet superapp contracts and commit the result):" : "Regenerated (review the diff, a contract change is a change for its consumers):");
        foreach (var path in changed)
        {
            output.Line($"  {path}");
        }
    }
}
