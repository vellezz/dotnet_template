using SuperApp.Cli.Doctor.Rules;

namespace SuperApp.Cli.Doctor;

/// <summary>The rules of <c>dotnet superapp doctor</c>, in the order they run and are reported.</summary>
/// <remarks>A new rule is a class implementing <see cref="IDoctorRule"/> in <c>Doctor/Rules</c>, added here and covered by a test.</remarks>
internal static class DoctorRules
{
    /// <summary>All rules: solution and registrations first (they break builds and deployments), documentation last.</summary>
    public static IReadOnlyList<IDoctorRule> All { get; } =
    [
        new SolutionFilesRule(),
        new ServiceRegistrationRule(),
        new BffRegistrationRule(),
        new PortsRule(),
        new EventIdRule(),
        new ToolVersionRule(),
        new DocumentationLinksRule(),
        new DocumentationPathsRule(),
    ];
}
