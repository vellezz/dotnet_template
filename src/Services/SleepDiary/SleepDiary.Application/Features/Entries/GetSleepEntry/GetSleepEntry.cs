using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace SleepDiary.Application.Features.Entries.GetSleepEntry;

/// <summary>
/// Query: returns the current user's sleep diary entry for the given day.
/// </summary>
/// <remarks>
/// <para>
/// Requires the scope <see cref="SleepDiaryScopes.EntryRead"/>. The handler lives in Infrastructure (<c>GetSleepEntryHandler</c>, ADR-0026)
/// and reads the read model directly, always filtered by the caller's <c>sub</c>; entries of other users are indistinguishable from
/// missing ones.
/// </para>
/// <para>
/// Result: <see cref="SleepEntryDto"/>, or <see cref="Domain.Entries.SleepEntryErrors.NotFound"/> (<c>sleepdiary.entry.not_found</c>, HTTP 404)
/// when the user has no entry for that day, plus the pipeline errors <c>auth.missing_scope</c> and <c>auth.unauthenticated</c> (HTTP 403).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var result = await sender.Send(new GetSleepEntry(new DateOnly(2026, 9, 30)), cancellationToken);
/// </code>
/// </example>
/// <param name="Date">Date of the entry: the day the user woke up.</param>
[RequiresScope(SleepDiaryScopes.EntryRead)]
public sealed record GetSleepEntry(DateOnly Date) : IQuery<Result<SleepEntryDto>>;
