using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// Recognizes SQL Server unique index and unique key violations in EF Core save exceptions and extracts the violated index or constraint name.
/// </summary>
/// <remarks>
/// SQL Server reports a duplicate in a unique index with error 2601 (<c>... with unique index 'IX_Name'</c>) and a duplicate in a primary or
/// unique key constraint with error 2627 (<c>Violation of UNIQUE KEY constraint 'UQ_Name'</c>). The names are read from the message, which
/// SQL Server always formats with the object names in single quotes.
/// </remarks>
internal static partial class UniqueConstraintViolation
{
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int UniqueConstraintViolated = 2627;

    /// <summary>Tells whether <paramref name="exception"/> was caused by a unique index or key violation.</summary>
    /// <param name="exception">The exception thrown by <c>SaveChangesAsync</c>.</param>
    /// <param name="name">The name of the violated index or constraint, or <see langword="null"/> when the exception has another cause.</param>
    /// <returns><see langword="true"/> for a unique violation.</returns>
    public static bool TryGetName(DbUpdateException exception, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (exception.InnerException is not SqlException sql)
        {
            return false;
        }

        var match = sql.Number switch
        {
            DuplicateKeyInUniqueIndex => UniqueIndexName().Match(sql.Message),
            UniqueConstraintViolated => ConstraintName().Match(sql.Message),
            _ => null,
        };

        if (match is not { Success: true })
        {
            return false;
        }

        name = match.Groups["name"].Value;
        return true;
    }

    [GeneratedRegex(@"unique index '(?<name>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex UniqueIndexName();

    [GeneratedRegex(@"constraint '(?<name>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex ConstraintName();
}
