namespace SleepDiary.Api.Controllers;

/// <summary>Response body of <c>201 Created</c> after a new sleep diary entry was recorded.</summary>
/// <param name="Id">
/// Technical identifier of the new entry (GUID). The entry is addressed by its date in all other endpoints; the identifier is informational
/// and matches <c>entryId</c> of the <c>SleepEntryRecordedV1</c> integration event.
/// </param>
public sealed record CreatedResponse(Guid Id);
