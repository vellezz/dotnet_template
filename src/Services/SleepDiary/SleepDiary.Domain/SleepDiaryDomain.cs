using System.Reflection;

namespace SleepDiary.Domain;

/// <summary>
/// Marker of the SleepDiary domain assembly, used wherever infrastructure needs to scan the domain model.
/// </summary>
/// <remarks>
/// The write and read <c>DbContext</c>s pass <see cref="Assembly"/> to the <c>SuperApp.Framework</c> EF conventions, which find the strongly typed IDs
/// and single-value objects (<see cref="Entries.SleepEntryId"/>, <see cref="Entries.UserId"/>, <see cref="Entries.SleepQuality"/>) and register
/// their value conversions. Architecture tests use it to check that the domain has no technical dependencies.
/// </remarks>
public static class SleepDiaryDomain
{
    /// <summary>Gets the assembly containing the SleepDiary domain model.</summary>
    public static Assembly Assembly => typeof(SleepDiaryDomain).Assembly;
}
