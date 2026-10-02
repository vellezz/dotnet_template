namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Registers and unregisters the repository of an aggregate in <c>Add{Service}Core</c> of <c>InfrastructureServiceCollectionExtensions</c>.</summary>
/// <remarks>
/// The line <c>services.AddScoped&lt;I{Aggregate}Repository, {Aggregate}Repository&gt;();</c> goes after the last repository registration, or
/// after <c>services.AddAppFeatureFlags(configuration);</c> for the first one, together with the usings of the domain folder and of
/// <c>Persistence.Write.Repositories</c>.
/// </remarks>
internal static class RegistrationEditor
{
    /// <summary>Path of the registration file of a service.</summary>
    /// <param name="directory">Directory of the service.</param>
    /// <param name="service">Service name.</param>
    /// <returns>The path relative to the repository root.</returns>
    public static string Path(string directory, string service) => $"{directory}/{service}.Infrastructure/InfrastructureServiceCollectionExtensions.cs";

    /// <summary>Adds the registration of a repository.</summary>
    /// <param name="content">Content of the registration file.</param>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder of the aggregate.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <returns>The new content.</returns>
    /// <exception cref="ScaffoldException">Neither a repository registration nor <c>AddAppFeatureFlags</c> can be found.</exception>
    public static string AddRepository(string content, string service, string feature, string aggregate)
    {
        var registration = $"        services.AddScoped<I{aggregate}Repository, {aggregate}Repository>();";
        if (!content.Contains(registration.Trim(), StringComparison.Ordinal))
        {
            content = TextEdits.HasLine(content, IsRepositoryRegistration)
                ? TextEdits.InsertAfterLast(content, IsRepositoryRegistration, [registration], "the repository registrations")
                : TextEdits.InsertAfterLast(content, line => line.Trim() == "services.AddAppFeatureFlags(configuration);", [registration], "services.AddAppFeatureFlags(configuration);");
        }

        foreach (var directive in (string[])[$"using {service}.Domain.{feature};", $"using {service}.Infrastructure.Persistence.Write.Repositories;"])
        {
            if (!TextEdits.HasLine(content, line => line == directive))
            {
                content = TextEdits.InsertAfterLast(content, TextEdits.IsUsingDirective, [directive], "the using directives");
            }
        }

        return content;
    }

    /// <summary>Removes the registration of a repository and the usings no other registration needs.</summary>
    /// <param name="content">Content of the registration file.</param>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder of the aggregate.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <param name="featureStillUsed">Whether other aggregates of the same domain folder stay registered.</param>
    /// <returns>The new content.</returns>
    public static string RemoveRepository(string content, string service, string feature, string aggregate, bool featureStillUsed)
    {
        content = TextEdits.RemoveLines(content, line => line.Trim() == $"services.AddScoped<I{aggregate}Repository, {aggregate}Repository>();");
        if (!featureStillUsed)
        {
            content = TextEdits.RemoveLines(content, line => line == $"using {service}.Domain.{feature};");
        }

        if (!TextEdits.HasLine(content, IsRepositoryRegistration))
        {
            content = TextEdits.RemoveLines(content, line => line == $"using {service}.Infrastructure.Persistence.Write.Repositories;");
        }

        return content;
    }

    private static bool IsRepositoryRegistration(string line) =>
        line.TrimStart().StartsWith("services.AddScoped<I", StringComparison.Ordinal) && line.Contains("Repository>();", StringComparison.Ordinal);
}
