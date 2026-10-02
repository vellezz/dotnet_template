using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor;

/// <summary>A <see cref="IDoctorRule"/> whose findings can be fixed mechanically by <c>doctor --fix</c>.</summary>
/// <remarks>
/// A fix must be safe to run on a consistent repository (it changes nothing then) and must only touch files the rule owns, e.g. the
/// solution rule regenerates <c>SuperApp.sln</c> from <c>SuperApp.slnx</c> and nothing else.
/// </remarks>
internal interface IFixableDoctorRule : IDoctorRule
{
    /// <summary>Fixes what <see cref="IDoctorRule.Check"/> reports.</summary>
    /// <param name="model">The scanned repository.</param>
    /// <returns>Changed files relative to the repository root; empty when nothing had to change.</returns>
    IReadOnlyList<string> Fix(RepositoryModel model);
}
