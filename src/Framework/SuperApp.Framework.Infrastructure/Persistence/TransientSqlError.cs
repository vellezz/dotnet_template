using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// Tells database errors that may go away on their own (timeouts, lost connections, deadlocks, a database failing over) apart from
/// errors that will repeat until someone changes code, schema or configuration.
/// </summary>
/// <remarks>
/// <para>
/// Used where a transient failure is handled differently from a permanent one, for example by <c>FailSafeCache</c>, which serves a stale value
/// only while the database is temporarily unavailable. A permanent error (invalid column after a missed migration, missing permission,
/// wrong login) must surface instead of being hidden behind stale data.
/// </para>
/// <para>
/// SQL Server errors are classified by number (the same list of connection, timeout, deadlock and failover errors that SqlClient's
/// built-in retry logic and EF Core's <c>SqlServerRetryingExecutionStrategy</c> treat as transient). Errors of other providers rely on
/// <see cref="DbException.IsTransient"/>.
/// </para>
/// </remarks>
internal static class TransientSqlError
{
    private static readonly HashSet<int> TransientNumbers =
    [
        -2,     // client timeout
        20,     // instance does not support encryption (seen while the server restarts)
        64,     // connection dropped
        233,    // no process on the other end of the pipe
        1205,   // deadlock victim
        4060,   // cannot open database (database starting or failing over)
        4221,   // login to a read-secondary failed during failover
        10053,  // transport-level error: connection aborted
        10054,  // transport-level error: connection reset
        10060,  // network timeout
        10928,  // resource limit reached
        10929,  // resource limit reached
        11001,  // host not found (DNS not available yet)
        40143,  // failover in progress
        40197,  // service error while processing the request
        40501,  // service busy
        40613,  // database not currently available
        49918,  // not enough resources
        49919,  // too many operations in progress
        49920,  // too many operations in progress
    ];

    /// <summary>Tells whether <paramref name="exception"/> is a database error worth waiting out rather than reporting.</summary>
    /// <param name="exception">An exception thrown by a query or command; non-database exceptions return <see langword="false"/>.</param>
    /// <returns>
    /// <see langword="true"/> for a SQL Server error whose number is in the transient list, or another provider's error that reports itself as
    /// transient; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsTransient(Exception exception) => exception switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(error => TransientNumbers.Contains(error.Number)),
        DbException db => db.IsTransient,
        _ => false,
    };
}
