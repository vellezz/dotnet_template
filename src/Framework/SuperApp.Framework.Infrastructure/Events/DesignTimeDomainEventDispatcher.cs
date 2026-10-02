using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Domain.Events;

namespace SuperApp.Framework.Infrastructure.Events;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> for processes that build write contexts but must never save aggregates: the EF Core design-time tools
/// (<c>dotnet ef migrations add</c>) and <c>SuperApp.Migrator</c>.
/// </summary>
/// <remarks>
/// Pass <see cref="Instance"/> in the service's <c>DesignTimeWriteDbContextFactory</c>; the migrator registers it as its dispatcher.
/// Applying migrations never dispatches domain events, so any call to <see cref="DispatchAsync"/> indicates a bug and throws.
/// </remarks>
public sealed class DesignTimeDomainEventDispatcher : IDomainEventDispatcher
{
    /// <summary>The single instance, passed to the write context constructor by the design-time factory.</summary>
    public static readonly DesignTimeDomainEventDispatcher Instance = new();

    /// <summary>Always throws: design-time tools and the migrator only build the model or apply migrations and must never save aggregates.</summary>
    /// <param name="domainEvent">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Zdarzenia domenowe nie są obsługiwane w czasie projektowania.");
}
