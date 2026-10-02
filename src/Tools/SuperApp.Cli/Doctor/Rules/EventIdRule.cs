using SuperApp.Cli.Repository;

namespace SuperApp.Cli.Doctor.Rules;

/// <summary>Every log event ID lies in the range of its component in the EventId register, is used once, and the register lists it.</summary>
/// <remarks>
/// Stable, unique event IDs are what alerts and log queries are built on (ADR-0008). Errors: an ID outside any range, in the range of
/// another component, or declared twice. Warnings: the "used" column of the register does not match the code (an ID missing from it, or
/// listed but not used), which misleads the next person choosing a free ID.
/// </remarks>
internal sealed class EventIdRule : IDoctorRule
{
    /// <inheritdoc />
    public string Id => "event-ids";

    /// <inheritdoc />
    public string Description => "Log event IDs are unique, lie in their component's range and are listed in docs/logowanie-eventid.md.";

    /// <inheritdoc />
    public IEnumerable<Finding> Check(RepositoryModel model, DoctorSettings settings)
    {
        foreach (var duplicate in model.EventIds.GroupBy(usage => usage.Id).Where(group => group.Count() > 1))
        {
            var places = string.Join(", ", duplicate.Select(usage => $"{usage.File}:{usage.Line}"));
            yield return new Finding(Id, Severity.Error, $"Event ID {duplicate.Key} is declared more than once: {places}.", duplicate.First().File, duplicate.First().Line,
                "Give all but one the next free ID of their component's range.");
        }

        foreach (var usage in model.EventIds)
        {
            var range = model.EventIdRanges.FirstOrDefault(candidate => candidate.Contains(usage.Id));
            if (range is null)
            {
                yield return new Finding(Id, Severity.Error, $"Event ID {usage.Id} of {usage.Project} is outside every range of {EventIdRegister.Path}.", usage.File, usage.Line,
                    $"Use an ID from the range of {usage.Project} (dotnet superapp list eventids), or add a range for it to the register.");
            }
            else if (!range.BelongsTo(usage.Project))
            {
                yield return new Finding(Id, Severity.Error, $"Event ID {usage.Id} of {usage.Project} lies in the range {range.Start}–{range.End} of another component ({range.Component}).", usage.File, usage.Line,
                    $"Use an ID from the range of {usage.Project} (dotnet superapp list eventids).");
            }
            else if (!range.Documented.Contains(usage.Id))
            {
                yield return new Finding(Id, Severity.Warning, $"Event ID {usage.Id} of {usage.Project} is not listed in the used column of {EventIdRegister.Path}.", EventIdRegister.Path,
                    Fix: $"Add {usage.Id} to the row {range.Start}–{range.End}.");
            }
        }

        var used = model.EventIds.Select(usage => usage.Id).ToHashSet();
        foreach (var range in model.EventIdRanges)
        {
            foreach (var id in range.Documented.Where(id => !used.Contains(id)).Order())
            {
                yield return new Finding(Id, Severity.Warning, $"Event ID {id} is listed in {EventIdRegister.Path} but no [LoggerMessage] uses it.", EventIdRegister.Path,
                    Fix: $"Remove {id} from the row {range.Start}–{range.End}.");
            }
        }
    }
}
