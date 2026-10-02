namespace SuperApp.Cli.Repository;

/// <summary>A project in a solution file together with the solution folder it is shown in.</summary>
/// <param name="Folder">Solution folder path without leading and trailing <c>/</c>, e.g. <c>src/Services/Knowledge</c>; empty for the root.</param>
/// <param name="ProjectPath">Path of the <c>.csproj</c> relative to the repository root, with <c>/</c>.</param>
internal sealed record SolutionEntry(string Folder, string ProjectPath);
