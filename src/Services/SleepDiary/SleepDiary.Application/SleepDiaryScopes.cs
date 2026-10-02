namespace SleepDiary.Application;

/// <summary>
/// OAuth scopes of the SleepDiary API, named by the convention <c>{service}.{resource}.{action}</c> (ADR-0012, ADR-0029).
/// </summary>
/// <remarks>
/// <para>
/// Commands and queries declare the scope they need with <c>[RequiresScope(...)]</c>; the authorization pipeline behavior returns
/// <c>auth.missing_scope</c> (HTTP 403) when the caller's token lacks it. The gateways may enforce the same scopes per route (coarse-grained).
/// </para>
/// <para>
/// A scope only says what kind of operation is allowed; it never grants access to other users' entries. Every use case works exclusively on
/// the diary of the token's subject.
/// </para>
/// <para>The scopes must be defined in the CIAM for the <c>sleepdiary-api</c> resource and granted to the client applications.</para>
/// </remarks>
public static class SleepDiaryScopes
{
    /// <summary>Read the caller's own diary entries: one day's entry and the list of a date range.</summary>
    public const string EntryRead = "sleepdiary.entry.read";

    /// <summary>Record, update and delete the caller's own diary entries.</summary>
    public const string EntryWrite = "sleepdiary.entry.write";
}
