namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.MaterialCompletions</c>: a material marked as completed by a user.</summary>
internal sealed class MaterialCompletionRow
{
    /// <summary>Identifier of the completion (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Subject (<c>sub</c> claim) of the user.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Identifier of the completed material; no foreign key to <c>Materials</c>.</summary>
    public Guid MaterialId { get; init; }

    /// <summary>Moment the material was first marked as completed.</summary>
    public DateTimeOffset CompletedAt { get; init; }
}
