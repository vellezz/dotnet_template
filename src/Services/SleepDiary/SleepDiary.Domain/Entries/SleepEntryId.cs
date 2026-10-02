using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace SleepDiary.Domain.Entries;

/// <summary>
/// Strongly typed identifier of a <see cref="SleepEntry"/>: a wrapper around a <see cref="Guid"/> (version 7) that cannot be mixed up
/// with other identifiers (ADR-0023).
/// </summary>
/// <remarks>
/// <para>
/// It is the technical primary key of the entry and is returned to clients when an entry is recorded, but the API and the repository address
/// entries by their natural key, owner + date. EF Core, JSON and OpenAPI conversions are provided by conventions in <c>SuperApp.Framework</c>,
/// so no mapping code is needed.
/// </para>
/// <para>
/// Use <see cref="New"/> for a new entry, <see cref="Create"/> for untrusted input and <see cref="FromTrusted"/> only for values that are
/// already known to be valid. Never use <c>default(SleepEntryId)</c>: it wraps <see cref="Guid.Empty"/> (analyzer APP002 reports it).
/// </para>
/// </remarks>
/// <seealso cref="SleepEntry"/>
public readonly record struct SleepEntryId : IStronglyTypedId<SleepEntryId, Guid>
{
    private SleepEntryId(Guid value) => Value = value;

    /// <summary>
    /// Gets the underlying GUID; never <see cref="Guid.Empty"/> for an identifier created by <see cref="New"/> or <see cref="Create"/>.
    /// </summary>
    public Guid Value { get; }

    /// <summary>Creates a new unique identifier for a new entry (a time-ordered GUID version 7, which keeps the clustered index compact).</summary>
    /// <returns>A new entry identifier.</returns>
    public static SleepEntryId New() => new(Guid.CreateVersion7());

    /// <summary>Creates an identifier from an untrusted value, for example one received from a client.</summary>
    /// <param name="value">The GUID to wrap; must not be <see cref="Guid.Empty"/>.</param>
    /// <returns>
    /// The identifier, or a validation error <c>sleepdiary.entry.invalid_id</c> (HTTP 400) when <paramref name="value"/> is <see cref="Guid.Empty"/>.
    /// </returns>
    public static Result<SleepEntryId> Create(Guid value) =>
        value == Guid.Empty ? Error.Validation("sleepdiary.entry.invalid_id", "Niepoprawny identyfikator wpisu.") : new SleepEntryId(value);

    /// <summary>Wraps a value without validation. Use only for values that are already valid, for example ones read from the database.</summary>
    /// <param name="value">A trusted, non-empty GUID.</param>
    /// <returns>The entry identifier.</returns>
    public static SleepEntryId FromTrusted(Guid value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
