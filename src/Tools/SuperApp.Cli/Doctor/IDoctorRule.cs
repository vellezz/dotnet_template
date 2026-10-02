using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor;

/// <summary>A consistency check of the repository run by <c>dotnet superapp doctor</c>.</summary>
/// <remarks>
/// <para>
/// A rule only reads: it compares parts of the <see cref="RepositoryModel"/> with each other and with the files, and returns
/// <see cref="Finding"/>s. Rules are independent and run in the order of <see cref="DoctorRules.All"/>.
/// </para>
/// <para>
/// A rule whose problems can be fixed mechanically also implements <see cref="IFixableDoctorRule"/>; <c>doctor --fix</c> runs the fix and
/// then checks again.
/// </para>
/// </remarks>
internal interface IDoctorRule
{
    /// <summary>Stable identifier in kebab-case, used in output and in <c>--rule</c>.</summary>
    string Id { get; }

    /// <summary>One sentence about what the rule checks and why.</summary>
    string Description { get; }

    /// <summary>Checks the repository.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <param name="settings">Settings of <c>doctor</c> from <c>.config/superapp-doctor.json</c>.</param>
    /// <returns>The problems found; empty when the repository is consistent.</returns>
    IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings);
}
