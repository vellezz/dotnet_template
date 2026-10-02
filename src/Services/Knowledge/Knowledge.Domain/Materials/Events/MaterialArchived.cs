using SuperApp.Framework.Domain.Events;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Materials.Events;

/// <summary>
/// Domain event: a material has been archived and will never change again. Raised by <see cref="Material.Archive"/> on the transition
/// to <see cref="PublicationStatus.Archived"/> (not when the material was already archived).
/// </summary>
/// <remarks>
/// Dispatched when the unit of work saves the command (ADR-0027). <c>MaterialArchivedTranslator</c> turns it into the integration event
/// <c>MaterialArchivedV1</c> with <see cref="ArchivedAt"/>, published through the outbox in the same transaction; <c>Knowledge.Worker</c> consumes it and removes the
/// material from all users' favorites (a deliberate exception to "one transaction modifies one aggregate", ADR-0028).
/// </remarks>
/// <param name="MaterialId">Identifier of the archived material.</param>
/// <param name="ArchivedAt">
/// Moment of archiving: the <c>now</c> passed to <see cref="Material.Archive"/>, which is also stored as <see cref="Material.UpdatedAt"/>.
/// </param>
public sealed record MaterialArchived(MaterialId MaterialId, DateTimeOffset ArchivedAt) : IDomainEvent;
