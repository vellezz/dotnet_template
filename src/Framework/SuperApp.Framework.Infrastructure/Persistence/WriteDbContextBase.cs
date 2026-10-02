using System.Reflection;
using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Domain.Aggregates;
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Infrastructure.Persistence.Conventions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// Base class of a service's write database context. It is the unit of work of every command: it collects domain events from tracked
/// aggregates, dispatches them, and saves aggregates and outbox messages in one operation (ADR-0003, ADR-0027).
/// </summary>
/// <remarks>
/// <para>Save flow, all within the transaction opened by the transaction pipeline behavior:</para>
/// <list type="number">
///   <item><description>Take the domain events of every tracked aggregate and clear them.</description></item>
///   <item><description>Dispatch each event to its <see cref="IDomainEventHandler{TEvent}"/>s (they may publish integration events, which go to the outbox).</description></item>
///   <item><description>Write all changes, including outbox rows, with a single <c>SaveChangesAsync</c>.</description></item>
/// </list>
/// <para>
/// The write context is the only owner of the service schema and its migrations; it also contains the MassTransit inbox and outbox tables.
/// Entity configurations (<c>IEntityTypeConfiguration&lt;T&gt;</c>) are applied only from the derived context's namespace and its child
/// namespaces (by convention <c>Persistence/Write/Configurations</c>), keeping them apart
/// from read model configurations. Synchronous <c>SaveChanges</c> throws, because it would skip asynchronous event dispatch.
/// </para>
/// <para>
/// Unique index violations are returned by <see cref="IUnitOfWork.SaveChangesAsync"/> as <see cref="ErrorType.Conflict"/> errors; a derived
/// context maps its index names to errors of its own context by overriding <see cref="UniqueConstraintErrors"/>. Optimistic concurrency
/// conflicts become the <see cref="ConcurrencyConflict"/> error when this context opened the transaction and are rethrown otherwise
/// (a consumer's message is then retried).
/// Actions registered with <see cref="IUnitOfWork.OnCommitted"/> run after the commit, triggered by <see cref="AfterCommitInterceptor"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class KnowledgeWriteDbContext(DbContextOptions&lt;KnowledgeWriteDbContext&gt; options, IDomainEventDispatcher dispatcher)
///     : WriteDbContextBase(options, dispatcher)
/// {
///     public const string SchemaName = "knowledge";
///
///     protected override string Schema =&gt; SchemaName;
///
///     protected override Assembly DomainAssembly =&gt; KnowledgeDomain.Assembly;
/// }
/// </code>
/// </example>
/// <param name="options">Context options configured by <c>AddAppPersistence</c> (the <c>Write</c> connection string, migrations history in the service schema).</param>
/// <param name="dispatcher">Dispatcher of domain events, resolved from the same DI scope so that handlers share this context and its transaction.</param>
public abstract class WriteDbContextBase(DbContextOptions options, IDomainEventDispatcher dispatcher)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Gets the service schema (for example <c>knowledge</c>) used as the default schema of all tables (ADR-0021).</summary>
    protected abstract string Schema { get; }

    /// <summary>Gets the domain assembly whose strongly typed IDs and value objects get automatic value conversions.</summary>
    protected abstract Assembly DomainAssembly { get; }

    /// <inheritdoc />
    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    /// <inheritdoc />
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = new EfUnitOfWorkTransaction(this, await Database.BeginTransactionAsync(cancellationToken));
        _ownTransaction = transaction;
        return transaction;
    }

    /// <summary>
    /// Gets the error returned by <see cref="IUnitOfWork.SaveChangesAsync"/> when another command changed or deleted the same aggregate
    /// after this command loaded it (its <c>rowversion</c> no longer matches): <c>persistence.concurrency_conflict</c>, HTTP 409.
    /// </summary>
    public static Error ConcurrencyConflict { get; } =
        Error.Conflict("persistence.concurrency_conflict", "Zasób został w międzyczasie zmieniony przez kogoś innego. Odśwież dane i spróbuj ponownie.");

    // The transaction opened by BeginTransactionAsync while it is open; null inside a transaction owned by someone else (a consumer).
    private EfUnitOfWorkTransaction? _ownTransaction;

    private readonly List<Func<CancellationToken, Task>> _afterCommitActions = [];

    /// <summary>
    /// Gets the errors returned when a unique index or key of this context is violated, keyed by index or constraint name
    /// (for example <c>IX_Categories_Slug</c>); indexes not listed produce the generic <c>persistence.duplicate</c> conflict.
    /// </summary>
    /// <remarks>
    /// Map every unique index that users can hit through a race to the same error the handler returns when it detects the duplicate
    /// itself, so clients see one consistent answer. Name the indexes explicitly in the entity configuration (<c>HasDatabaseName</c>)
    /// when the default name is not stable.
    /// </remarks>
    protected virtual IReadOnlyDictionary<string, Error> UniqueConstraintErrors { get; } = new Dictionary<string, Error>();

    async Task<Result> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException) when (_ownTransaction is not null)
        {
            DiscardChanges();
            return ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (UniqueConstraintViolation.TryGetName(exception, out var name))
        {
            DiscardChanges();
            return UniqueConstraintErrors.TryGetValue(name, out var error)
                ? error
                : Error.Conflict("persistence.duplicate", "Zasób o podanych danych już istnieje.");
        }
    }

    // EF Core rolls the database back to the savepoint taken before a failed SaveChanges; the change tracker is brought back in line
    // here, keeping only MassTransit's inbox state, which the consumer pipeline still saves and commits.
    /// <inheritdoc />
    public void DiscardChanges()
    {
        _afterCommitActions.Clear();

        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                                     && entry.Entity is not MassTransit.EntityFrameworkCoreIntegration.InboxState)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public void OnCommitted(Func<CancellationToken, Task> action) => _afterCommitActions.Add(action);

    /// <summary>Removes and returns the actions registered with <see cref="OnCommitted"/>; used by <see cref="AfterCommitInterceptor"/>.</summary>
    /// <returns>The registered actions in registration order; the list is empty afterwards.</returns>
    internal IReadOnlyList<Func<CancellationToken, Task>> TakeAfterCommitActions()
    {
        var actions = _afterCommitActions.ToArray();
        _afterCommitActions.Clear();
        return actions;
    }

    /// <summary>
    /// Dispatches the domain events collected from tracked aggregates and then saves all changes (aggregates and outbox messages) (ADR-0027).
    /// </summary>
    /// <remarks>
    /// Called through <see cref="IUnitOfWork.SaveChangesAsync"/> by the transaction pipeline behavior. Dispatch is a single pass: events raised
    /// while handlers run are not dispatched in this save.
    /// </remarks>
    /// <param name="acceptAllChangesOnSuccess">Whether the change tracker accepts the changes after a successful save (EF Core default: <see langword="true"/>).</param>
    /// <param name="cancellationToken">Cancellation of the command.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IAggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            await dispatcher.DispatchAsync(domainEvent, cancellationToken);
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Not supported: saving synchronously would skip the asynchronous dispatch of domain events. Use <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int SaveChanges() => throw SynchronousSaveNotSupported();

    /// <summary>Not supported: saving synchronously would skip the asynchronous dispatch of domain events. Use <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    /// <param name="acceptAllChangesOnSuccess">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw SynchronousSaveNotSupported();

    /// <summary>
    /// Sets the default schema, applies the entity configurations declared in the derived context's namespace or its child namespaces, and adds the MassTransit
    /// inbox and outbox entities.
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, IsInContextNamespace);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }

    /// <summary>Adds value conversions for all strongly typed IDs and single-value objects of <see cref="DomainAssembly"/> (ADR-0023, ADR-0024).</summary>
    /// <param name="configurationBuilder">The EF Core conventions builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddSingleValueObjectConversions(DomainAssembly);

    private bool IsInContextNamespace(Type configurationType) =>
        configurationType.Namespace is { } ns
        && (ns == GetType().Namespace || ns.StartsWith(GetType().Namespace + ".", StringComparison.Ordinal));

    private static NotSupportedException SynchronousSaveNotSupported() =>
        new("Zapis wyłącznie przez IUnitOfWork.SaveChangesAsync (ADR-0027).");

    // Disposing without commit rolls the transaction back; the registered after-commit actions of the failed command go with it.
    private sealed class EfUnitOfWorkTransaction(WriteDbContextBase context, IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            _committed = true;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                _ = context.TakeAfterCommitActions();
            }

            context._ownTransaction = null;
            return transaction.DisposeAsync();
        }
    }
}
