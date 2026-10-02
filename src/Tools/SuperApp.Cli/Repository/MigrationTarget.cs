namespace SuperApp.Cli.Repository;

/// <summary>A write context that owns migrations (ADR-0004): the gateway or a domain service.</summary>
/// <param name="Name">Name used on the command line: <c>Gateway</c> or the service name, e.g. <c>Knowledge</c>.</param>
/// <param name="Context">Type name of the context, e.g. <c>KnowledgeWriteDbContext</c>.</param>
/// <param name="Project">Directory of the project with the context and its design-time factory, relative to the repository root.</param>
/// <param name="MigrationsDirectory">Directory of the migrations relative to <paramref name="Project"/>, e.g. <c>Migrations</c>.</param>
internal sealed record MigrationTarget(string Name, string Context, string Project, string MigrationsDirectory)
{
    /// <summary>Directory of the migrations relative to the repository root.</summary>
    public string MigrationsPath => $"{Project}/{MigrationsDirectory}";
}
