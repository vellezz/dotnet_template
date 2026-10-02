using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Tests;

/// <summary>A throw-away repository in a temporary directory with just the files a test needs; deleted on dispose.</summary>
internal sealed class TestRepository : IDisposable
{
    public TestRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "superapp-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Write(SolutionFiles.Slnx, "<Solution>\n</Solution>\n");
    }

    public string Root { get; }

    public TestRepository Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public TestRepository Project(string relativePath) => Write(relativePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

    public TestRepository LaunchProfile(string projectDirectory, string profile, string url) =>
        Write($"{projectDirectory}/Properties/launchSettings.json", $$"""{ "profiles": { "{{profile}}": { "applicationUrl": "{{url}}" } } }""");

    public RepositoryModel Scan() => RepositoryScanner.Scan(Root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows; the temporary directory is cleaned up by the OS.
        }
    }
}
