using System.Text.RegularExpressions;

namespace SuperApp.Cli.Repository;

/// <summary>Finds the contexts that own migrations, in the order the Migrator applies them, and the migrations each one has.</summary>
/// <remarks>
/// <para>
/// The order comes from the <c>contexts</c> array of <c>SuperApp.Migrator/Program.cs</c> (gateway first, then the services), so a script
/// for the DBA runs in the same order as the Migrator job. A service has a target when <c>{Service}.Infrastructure</c> contains
/// <c>{Service}WriteDbContext.cs</c>; its migrations live in <c>{Service}.Infrastructure/Migrations</c>, the gateway's in
/// <c>SuperApp.Gateway/Persistence/Migrations</c>.
/// </para>
/// <para>Migrations are read from the <c>[Migration("…")]</c> attributes of the designer files, without building or a database.</para>
/// </remarks>
internal static partial class MigrationTargets
{
    private const string MigratorProgram = "src/Migrator/SuperApp.Migrator/Program.cs";

    /// <summary>Returns the targets of the repository, in Migrator order; targets the Migrator does not list come last.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <returns>The targets.</returns>
    public static IReadOnlyList<MigrationTarget> All(RepositoryModel model)
    {
        var targets = new List<MigrationTarget> { new("Gateway", "GatewayDbContext", "src/Gateway/SuperApp.Gateway", "Persistence/Migrations") };
        foreach (var service in model.Services)
        {
            var project = $"{service.Directory}/{service.Name}.Infrastructure";
            if (model.Files.Files(project, $"{service.Name}WriteDbContext.cs").Count > 0)
            {
                targets.Add(new MigrationTarget(service.Name, $"{service.Name}WriteDbContext", project, "Migrations"));
            }
        }

        var order = ContextsArray().Match(model.Files.ReadOrEmpty(MigratorProgram)) is { Success: true } match
            ? TypeOf().Matches(match.Groups[1].Value).Select(type => type.Groups[1].Value).ToList()
            : [];
        return [.. targets.OrderBy(target => order.IndexOf(target.Context) is var index and >= 0 ? index : int.MaxValue)];
    }

    /// <summary>Finds a target by its command-line name, ignoring case.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="name"><c>Gateway</c> or a service name.</param>
    /// <returns>The target, or <see langword="null"/>.</returns>
    public static MigrationTarget? Find(RepositoryModel model, string name) =>
        All(model).FirstOrDefault(target => string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lists the migrations of a target, oldest first.</summary>
    /// <param name="files">Files of the repository.</param>
    /// <param name="target">The target.</param>
    /// <returns>Migration identifiers such as <c>20260929233807_Initial</c>.</returns>
    public static IReadOnlyList<string> Migrations(RepositoryFiles files, MigrationTarget target) =>
        [.. files.Files(target.MigrationsPath, "*.Designer.cs")
            .Select(file => MigrationAttribute().Match(files.ReadOrEmpty(file)))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)];

    [GeneratedRegex("Type\\[\\] contexts = \\[(.*?)\\];")]
    private static partial Regex ContextsArray();

    [GeneratedRegex("typeof\\((\\w+)\\)")]
    private static partial Regex TypeOf();

    [GeneratedRegex("\\[Migration\\(\"([^\"]+)\"\\)\\]")]
    private static partial Regex MigrationAttribute();
}
