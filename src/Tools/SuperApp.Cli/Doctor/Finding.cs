namespace SuperApp.Cli.Doctor;

/// <summary>One problem found by a <see cref="IDoctorRule"/>, with the place and a ready-made way to fix it.</summary>
/// <remarks>
/// Messages are written for a person and for an assistant such as Copilot alike: <see cref="Message"/> says what is wrong in one sentence,
/// <see cref="Fix"/> says exactly what to run or change, so the problem can be fixed without searching the documentation.
/// </remarks>
/// <param name="Rule">Identifier of the rule, e.g. <c>service-registration</c>.</param>
/// <param name="Severity">Whether the finding fails <c>doctor</c>.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="File">File the finding is about, relative to the repository root; <see langword="null"/> when it concerns several files.</param>
/// <param name="Line">1-based line in <paramref name="File"/>, when known.</param>
/// <param name="Fix">What to run or change; <see langword="null"/> when there is no mechanical fix.</param>
internal sealed record Finding(string Rule, Severity Severity, string Message, string? File = null, int? Line = null, string? Fix = null);
