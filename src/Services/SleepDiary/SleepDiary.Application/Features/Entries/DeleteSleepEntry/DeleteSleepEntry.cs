using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;

namespace SleepDiary.Application.Features.Entries.DeleteSleepEntry;

/// <summary>
/// Command: permanently deletes the current user's sleep diary entry for the given day.
/// </summary>
/// <remarks>
/// <para>
/// Requires the scope <see cref="SleepDiaryScopes.EntryWrite"/>. The entry is looked up by the caller's <c>sub</c> and <see cref="Date"/>,
/// so only the caller's own entry can be deleted. Afterwards a new entry may be recorded for the same day.
/// </para>
/// <para>
/// No domain or integration event is published: other contexts that consumed <see cref="Contracts.SleepEntryRecordedV1"/> are not told
/// about the deletion.
/// </para>
/// <para>
/// Result: success, or <see cref="Domain.Entries.SleepEntryErrors.NotFound"/> (<c>sleepdiary.entry.not_found</c>, HTTP 404) when the user
/// has no entry for that day, plus the pipeline errors <c>auth.missing_scope</c> and <c>auth.unauthenticated</c> (HTTP 403).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = await sender.Send(new DeleteSleepEntry(new DateOnly(2026, 9, 30)), cancellationToken);
/// </code>
/// </example>
/// <param name="Date">Date of the entry to delete: the day the user woke up.</param>
[RequiresScope(SleepDiaryScopes.EntryWrite)]
public sealed record DeleteSleepEntry(DateOnly Date) : ICommand;
