using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor;

/// <summary>Runs the <c>doctor</c> rules on a repository, optionally fixing first what can be fixed mechanically.</summary>
internal static class DoctorRunner
{
    /// <summary>Checks the repository rooted at <paramref name="root"/>.</summary>
    /// <param name="root">Full path of the repository root.</param>
    /// <param name="rules">Rules to run, usually <see cref="DoctorRules.All"/> or the ones selected by <c>--rule</c>.</param>
    /// <param name="fix">Whether to run <see cref="IFixableDoctorRule.Fix"/> of the selected rules before checking.</param>
    /// <returns>The findings after the optional fix, and the changed files.</returns>
    public static DoctorReport Run(string root, IReadOnlyList<IDoctorRule> rules, bool fix)
    {
        var fixedFiles = new List<string>();
        if (fix)
        {
            var before = RepositoryScanner.Scan(root);
            foreach (var rule in rules.OfType<IFixableDoctorRule>())
            {
                fixedFiles.AddRange(rule.Fix(before));
            }
        }

        var model = RepositoryScanner.Scan(root);
        var settings = DoctorSettings.Load(model.Files);
        return new DoctorReport([.. rules.SelectMany(rule => rule.Check(model, settings))], fixedFiles);
    }
}
