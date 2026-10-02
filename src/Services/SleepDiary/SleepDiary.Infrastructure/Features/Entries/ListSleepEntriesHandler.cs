using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Microsoft.EntityFrameworkCore;
using SleepDiary.Application.Features.Entries.ListSleepEntries;
using SleepDiary.Infrastructure.Persistence.Read;

namespace SleepDiary.Infrastructure.Features.Entries;

/// <summary>
/// Handles <see cref="ListSleepEntries"/> on the read side: loads the current user's entries in the date range and computes the averages (ADR-0026).
/// </summary>
/// <remarks>
/// Always filters by the caller's <c>sub</c> (ADR-0029). The rows of the range are loaded in one query (bounded to 366 days by the validator)
/// and the averages are computed in memory over the loaded entries: sleep minutes rounded to 1 decimal place, quality to 2 decimal places,
/// both <see langword="null"/> for an empty range.
/// </remarks>
/// <param name="db">Read-only, non-tracking context of the service.</param>
/// <param name="currentUser">The caller; supplies the owner filter.</param>
internal sealed class ListSleepEntriesHandler(SleepDiaryReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<ListSleepEntries, Result<SleepDiaryDto>>
{
    /// <inheritdoc />
    /// <returns>
    /// The diary for the range (possibly with no entries), or <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>)
    /// when the caller has no subject.
    /// </returns>
    public async Task<Result<SleepDiaryDto>> Handle(ListSleepEntries query, CancellationToken cancellationToken)
    {
        if (currentUser.Subject is not { } subject)
        {
            return AuthorizationErrors.Unauthenticated;
        }

        var rows = await db.Entries
            .Where(entry => entry.UserId == subject && entry.Date >= query.From && entry.Date <= query.To)
            .OrderBy(entry => entry.Date)
            .ToListAsync(cancellationToken);

        return new SleepDiaryDto(
            query.From,
            query.To,
            rows.Select(EntryMapping.ToDto).ToList(),
            rows.Count == 0 ? null : Math.Round(rows.Average(row => row.SleepMinutes), 1),
            rows.Count == 0 ? null : Math.Round(rows.Average(row => row.Quality), 2));
    }
}
