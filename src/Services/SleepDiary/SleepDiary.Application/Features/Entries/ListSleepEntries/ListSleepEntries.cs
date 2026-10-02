using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace SleepDiary.Application.Features.Entries.ListSleepEntries;

/// <summary>
/// Query: returns the current user's sleep diary for a range of days, with the entries and the average sleep time and quality.
/// </summary>
/// <remarks>
/// <para>
/// Requires the scope <see cref="SleepDiaryScopes.EntryRead"/>. The handler lives in Infrastructure (<c>ListSleepEntriesHandler</c>, ADR-0026)
/// and always filters by the caller's <c>sub</c>. The range is inclusive on both ends and matched against the entry date (the wake-up day).
/// Days without an entry are simply absent; they do not count towards the averages.
/// </para>
/// <para>
/// Validation (<c>ListSleepEntriesValidator</c>): both <see cref="From"/> and <see cref="To"/> must be given (<c>default(DateOnly)</c>,
/// <c>0001-01-01</c>, is what a missing query parameter binds to and is rejected), <see cref="To"/> must not be earlier than <see cref="From"/>,
/// and the range may cover at most 366 days including both ends. A violation returns <c>validation.failed</c> (HTTP 400) with field messages.
/// </para>
/// <para>
/// Result: <see cref="SleepDiaryDto"/> (never a not-found error; an empty range gives an empty list), plus the pipeline errors
/// <c>auth.missing_scope</c> and <c>auth.unauthenticated</c> (HTTP 403).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var week = await sender.Send(new ListSleepEntries(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27)), cancellationToken);
/// </code>
/// </example>
/// <param name="From">First day of the range (inclusive), compared with the entry date; required (must not be <c>0001-01-01</c>).</param>
/// <param name="To">
/// Last day of the range (inclusive); required (must not be <c>0001-01-01</c>), not earlier than <paramref name="From"/> and at most 365 days
/// after it.
/// </param>
[RequiresScope(SleepDiaryScopes.EntryRead)]
public sealed record ListSleepEntries(DateOnly From, DateOnly To) : IQuery<Result<SleepDiaryDto>>;
