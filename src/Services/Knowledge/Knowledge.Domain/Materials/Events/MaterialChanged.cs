using SuperApp.Framework.Domain.Events;

namespace Knowledge.Domain.Materials.Events;

/// <summary>
/// Domain event: something about a material has changed (details, content, categories, status). Raised by every successful change of
/// <see cref="Material"/>, including creation, publication and archiving.
/// </summary>
/// <remarks>
/// It carries no details on purpose; its job is cache invalidation (ADR-0020): <c>MaterialCacheInvalidation</c> removes, after the commit, all cache entries
/// tagged with the material. A material raises it at most once until the pending events are dispatched and cleared, so one command yields
/// one event even when it changes several things. It is internal to the Knowledge context and is not published as an integration event.
/// </remarks>
/// <param name="MaterialId">Identifier of the changed material.</param>
public sealed record MaterialChanged(MaterialId MaterialId) : IDomainEvent;
