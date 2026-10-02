using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Microsoft.EntityFrameworkCore;
using SleepDiary.Application.Features.Entries.GetSleepEntry;
using SleepDiary.Domain.Entries;
using SleepDiary.Infrastructure.Persistence.Read;

namespace SleepDiary.Infrastructure.Features.Entries;

/// <summary>
/// Handles <see cref="GetSleepEntry"/> on the read side: loads the current user's entry for the day from the read model (ADR-0026).
/// </summary>
/// <remarks>
/// Always filters by the caller's <c>sub</c> (ADR-0029), so another user's entry for the same day is reported as not found. No cache is used:
/// the data is per user and changes with every edit.
/// </remarks>
/// <param name="db">Read-only, non-tracking context of the service.</param>
/// <param name="currentUser">The caller; supplies the owner filter.</param>
internal sealed class GetSleepEntryHandler(SleepDiaryReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetSleepEntry, Result<SleepEntryDto>>
{
    /// <inheritdoc />
    /// <returns>
    /// The entry; <see cref="SleepEntryErrors.NotFound"/> (<c>sleepdiary.entry.not_found</c>) when the caller has no entry for the day;
    /// <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>) when the caller has no subject.
    /// </returns>
    public async Task<Result<SleepEntryDto>> Handle(GetSleepEntry query, CancellationToken cancellationToken)
    {
        if (currentUser.Subject is not { } subject)
        {
            return AuthorizationErrors.Unauthenticated;
        }

        var row = await db.Entries.FirstOrDefaultAsync(entry => entry.UserId == subject && entry.Date == query.Date, cancellationToken);
        return row is null ? SleepEntryErrors.NotFound : EntryMapping.ToDto(row);
    }
}
