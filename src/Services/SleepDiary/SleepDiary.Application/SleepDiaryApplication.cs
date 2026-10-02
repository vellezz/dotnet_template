using System.Reflection;

namespace SleepDiary.Application;

/// <summary>
/// Marker of the SleepDiary application assembly, used to register its use cases.
/// </summary>
/// <remarks>
/// <c>AddSleepDiaryCore</c> passes <see cref="Assembly"/> to <c>AddAppApplication</c>, which scans it for command handlers, validators and
/// domain event handlers (the Infrastructure assembly is scanned as well, for query handlers). Tests use it to check that every command has a handler.
/// </remarks>
public static class SleepDiaryApplication
{
    /// <summary>Gets the assembly containing the SleepDiary use cases.</summary>
    public static Assembly Assembly => typeof(SleepDiaryApplication).Assembly;
}
