using System.Xml;
using System.Xml.Linq;

namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Adds and removes projects in <c>SuperApp.slnx</c>, each in the solution folder that mirrors its directory.</summary>
/// <remarks>
/// A project <c>src/Services/Billing/Billing.Api/Billing.Api.csproj</c> goes into the folder <c>/src/Services/Billing/</c>; missing parent
/// folders are declared as empty <c>&lt;Folder/&gt;</c> elements, folders and projects are kept sorted. Unlike <c>dotnet sln add</c>, no
/// referenced project is added implicitly. Removing the last project of a folder removes the folder too.
/// </remarks>
internal static class SlnxEditor
{
    /// <summary>Adds projects that are not in the solution yet.</summary>
    /// <param name="content">Content of the <c>.slnx</c> file.</param>
    /// <param name="projects">Project paths relative to the repository root, with <c>/</c>.</param>
    /// <returns>The new content.</returns>
    public static string Add(string content, IEnumerable<string> projects)
    {
        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        var solution = document.Root!;
        foreach (var project in projects)
        {
            if (solution.Descendants("Project").Any(element => (string?)element.Attribute("Path") == project))
            {
                continue;
            }

            var folderName = FolderOf(project);
            var parts = folderName.Trim('/').Split('/');
            for (var length = 1; length <= parts.Length; length++)
            {
                Folder(solution, "/" + string.Join('/', parts.Take(length)) + "/");
            }

            var folder = Folder(solution, folderName);
            folder.Add(new XElement("Project", new XAttribute("Path", project)));
            var sorted = folder.Elements("Project").OrderBy(element => (string?)element.Attribute("Path"), StringComparer.Ordinal).ToList();
            folder.RemoveNodes();
            folder.Add(sorted);
        }

        return Write(document, content);
    }

    /// <summary>Removes projects and the folders that become empty.</summary>
    /// <param name="content">Content of the <c>.slnx</c> file.</param>
    /// <param name="projects">Project paths relative to the repository root, with <c>/</c>.</param>
    /// <returns>The new content.</returns>
    public static string Remove(string content, IEnumerable<string> projects)
    {
        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        var removed = projects.ToHashSet(StringComparer.Ordinal);
        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.Root!.Descendants("Project").Where(element => removed.Contains((string?)element.Attribute("Path") ?? string.Empty)).ToList())
        {
            affected.Add((string?)element.Parent?.Attribute("Name") ?? string.Empty);
            element.Remove();
        }

        foreach (var folder in document.Root.Elements("Folder").Where(folder => affected.Contains((string?)folder.Attribute("Name") ?? string.Empty) && !folder.Elements().Any()).ToList())
        {
            folder.Remove();
        }

        return Write(document, content);
    }

    /// <summary>Returns the solution folder of a project: the directory that contains the project's directory.</summary>
    /// <param name="project">Project path relative to the repository root.</param>
    /// <returns>The folder name with leading and trailing <c>/</c>, e.g. <c>/src/Services/Billing/</c>.</returns>
    public static string FolderOf(string project)
    {
        var parts = project.Split('/');
        return "/" + string.Join('/', parts.Take(parts.Length - 2)) + "/";
    }

    private static XElement Folder(XElement solution, string name)
    {
        var existing = solution.Elements("Folder").FirstOrDefault(folder => (string?)folder.Attribute("Name") == name);
        if (existing is not null)
        {
            return existing;
        }

        var created = new XElement("Folder", new XAttribute("Name", name));
        var next = solution.Elements("Folder").FirstOrDefault(folder => string.CompareOrdinal((string?)folder.Attribute("Name"), name) > 0);
        if (next is null)
        {
            var lastFolder = solution.Elements("Folder").LastOrDefault();
            if (lastFolder is null)
            {
                solution.AddFirst(created);
            }
            else
            {
                lastFolder.AddAfterSelf(created);
            }
        }
        else
        {
            next.AddBeforeSelf(created);
        }

        return created;
    }

    // Keeps the line ending of the original file (dotnet sln writes CRLF on Windows).
    private static string Write(XDocument document, string original)
    {
        foreach (var text in document.DescendantNodes().OfType<XText>().ToList())
        {
            text.Remove();
        }

        var newLine = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "  ", NewLineChars = newLine };
        using var writer = new StringWriter();
        using (var xml = XmlWriter.Create(writer, settings))
        {
            document.Save(xml);
        }

        return writer + newLine;
    }
}
