namespace SuperApp.Cli.Scaffolding.Editors;

/// <summary>Source code of new domain elements: an aggregate with its identifier, errors, repository, EF configuration and test.</summary>
/// <remarks>
/// <para>
/// The code follows the patterns of SleepDiary (ADR-0002, ADR-0023, ADR-0024, ADR-0032): a strongly typed ID as a
/// <c>readonly record struct</c> with <c>New</c>, <c>Create</c> and <c>FromTrusted</c>; errors as <c>Error</c> constants with stable codes;
/// one repository interface per aggregate in Domain, implemented in Infrastructure; EF configuration only in
/// <c>IEntityTypeConfiguration</c> with a row version for optimistic concurrency.
/// </para>
/// <para>It compiles and passes the analyzers as generated; the parts that need the domain are marked <c>TODO</c>.</para>
/// </remarks>
internal static class DomainCode
{
    /// <summary>The aggregate root.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder of the aggregate, e.g. <c>Invoices</c>.</param>
    /// <param name="aggregate">Aggregate name, e.g. <c>Invoice</c>.</param>
    /// <returns>The file content.</returns>
    public static string Aggregate(string service, string feature, string aggregate) => $$"""
        using SuperApp.Framework.Domain.Aggregates;
        using SuperApp.Framework.Domain.Results;

        namespace {{service}}.Domain.{{feature}};

        /// <summary>
        /// TODO: what a {{aggregate}} is in the language of the {{service}} domain and which invariants it protects.
        /// </summary>
        /// <remarks>
        /// TODO: lifecycle, rules and the domain events it raises. State changes only through methods named in the domain language, which
        /// return <see cref="Result"/> for a broken rule (ADR-0002, ADR-0015); one transaction changes one aggregate.
        /// </remarks>
        public sealed class {{aggregate}} : AggregateRoot<{{aggregate}}Id>
        {
            private {{aggregate}}({{aggregate}}Id id)
                : base(id)
            {
            }

            /// <summary>Gets the moment the {{aggregate}} was created (UTC, from <c>IClock</c>).</summary>
            public DateTimeOffset CreatedAt { get; private set; }

            /// <summary>Creates a new {{aggregate}}. TODO: the data it needs and the rules it checks.</summary>
            /// <param name="now">The current time from <c>IClock</c>; time is passed in, never read in the domain.</param>
            /// <returns>The new {{aggregate}} with a new identifier. TODO: every error with its code.</returns>
            public static Result<{{aggregate}}> Create(DateTimeOffset now) => new {{aggregate}}({{aggregate}}Id.New()) { CreatedAt = now };
        }

        """;

    /// <summary>The strongly typed identifier of the aggregate (ADR-0023).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <param name="code">Error code prefix, e.g. <c>billing.invoice</c>.</param>
    /// <returns>The file content.</returns>
    public static string Id(string service, string feature, string aggregate, string code) => $$"""
        using SuperApp.Framework.Domain.Results;
        using SuperApp.Framework.Domain.ValueObjects;

        namespace {{service}}.Domain.{{feature}};

        /// <summary>Identifier of a <see cref="{{aggregate}}"/>: a GUID that can never be confused with the identifier of another aggregate.</summary>
        /// <remarks>
        /// Created only through <see cref="New"/>, <see cref="Create"/> or <see cref="FromTrusted"/> (ADR-0023); <c>default</c> is rejected by the
        /// analyzer APP002. EF Core, JSON and OpenAPI map it to its <see cref="Guid"/> through the conventions of <c>SuperApp.Framework</c>.
        /// </remarks>
        public readonly record struct {{aggregate}}Id : IStronglyTypedId<{{aggregate}}Id, Guid>
        {
            private {{aggregate}}Id(Guid value) => Value = value;

            /// <summary>Gets the underlying GUID; never <see cref="Guid.Empty"/> for an identifier created by <see cref="New"/> or <see cref="Create"/>.</summary>
            public Guid Value { get; }

            /// <summary>Creates a new unique identifier (a time-ordered GUID version 7, which keeps the clustered index compact).</summary>
            /// <returns>A new identifier.</returns>
            public static {{aggregate}}Id New() => new(Guid.CreateVersion7());

            /// <summary>Creates an identifier from an untrusted value, for example one received from a client.</summary>
            /// <param name="value">The GUID to wrap; must not be <see cref="Guid.Empty"/>.</param>
            /// <returns>The identifier, or a validation error <c>{{code}}.invalid_id</c> (HTTP 400) for <see cref="Guid.Empty"/>.</returns>
            public static Result<{{aggregate}}Id> Create(Guid value) =>
                value == Guid.Empty ? Error.Validation("{{code}}.invalid_id", "Niepoprawny identyfikator.") : new {{aggregate}}Id(value);

            /// <summary>Wraps a value without validation; only for values that are already valid, for example ones read from the database.</summary>
            /// <param name="value">A trusted, non-empty GUID.</param>
            /// <returns>The identifier.</returns>
            public static {{aggregate}}Id FromTrusted(Guid value) => new(value);

            /// <inheritdoc />
            public override string ToString() => Value.ToString();
        }

        """;

    /// <summary>The errors of the aggregate (ADR-0015).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <param name="code">Error code prefix, e.g. <c>billing.invoice</c>.</param>
    /// <returns>The file content.</returns>
    public static string Errors(string service, string feature, string aggregate, string code) => $$"""
        using SuperApp.Framework.Domain.Results;

        namespace {{service}}.Domain.{{feature}};

        /// <summary>Errors of <see cref="{{aggregate}}"/>: stable codes <c>{{code}}.*</c> that clients branch on (ADR-0015).</summary>
        /// <remarks>TODO: one constant per broken rule, named after the rule; the type decides the HTTP status (Validation 400, NotFound 404, Conflict 409, BusinessRule 422).</remarks>
        public static class {{aggregate}}Errors
        {
            /// <summary>The {{aggregate}} does not exist or the caller may not see it (<c>{{code}}.not_found</c>, NotFound, HTTP 404).</summary>
            public static readonly Error NotFound = Error.NotFound("{{code}}.not_found", "Nie znaleziono.");
        }

        """;

    /// <summary>The repository port of the aggregate (one per aggregate, no generic repository).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <returns>The file content.</returns>
    public static string RepositoryInterface(string service, string feature, string aggregate) => $$"""
        namespace {{service}}.Domain.{{feature}};

        /// <summary>Write-side access to <see cref="{{aggregate}}"/> aggregates; implemented in Infrastructure on the write context.</summary>
        /// <remarks>
        /// Loaded aggregates are tracked: their changes are saved by the unit of work of the command (the transaction pipeline behavior),
        /// never by the repository. No <c>IQueryable</c>: reads for screens go through the read side (ADR-0003, ADR-0026).
        /// </remarks>
        public interface I{{aggregate}}Repository
        {
            /// <summary>Loads an aggregate for modification.</summary>
            /// <param name="id">Identifier of the aggregate.</param>
            /// <param name="cancellationToken">Cancels the database query.</param>
            /// <returns>The tracked aggregate, or <see langword="null"/> when it does not exist.</returns>
            Task<{{aggregate}}?> FindAsync({{aggregate}}Id id, CancellationToken cancellationToken);

            /// <summary>Registers a new aggregate; it is inserted when the command's changes are saved.</summary>
            /// <param name="aggregate">A new aggregate created by its factory method.</param>
            void Add({{aggregate}} aggregate);

            /// <summary>Marks an aggregate for deletion; it is deleted when the command's changes are saved.</summary>
            /// <param name="aggregate">An aggregate loaded by <see cref="FindAsync"/> in the same unit of work.</param>
            void Remove({{aggregate}} aggregate);
        }

        """;

    /// <summary>The EF Core configuration of the aggregate on the write context.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <param name="table">Table name, the aggregate in plural.</param>
    /// <returns>The file content.</returns>
    public static string Configuration(string service, string feature, string aggregate, string table) => $$"""
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using {{service}}.Domain.{{feature}};

        namespace {{service}}.Infrastructure.Persistence.Write.Configurations;

        /// <summary>Maps <see cref="{{aggregate}}"/> to the table <c>{{table}}</c> of the service schema.</summary>
        /// <remarks>
        /// TODO: lengths from the domain constants, value objects (complex properties), owned collections, indexes and check constraints.
        /// The identifier is mapped by the convention of <c>SuperApp.Framework</c>; the row version gives optimistic concurrency (409 on a lost update).
        /// </remarks>
        internal sealed class {{aggregate}}Configuration : IEntityTypeConfiguration<{{aggregate}}>
        {
            /// <inheritdoc />
            public void Configure(EntityTypeBuilder<{{aggregate}}> builder)
            {
                builder.ToTable("{{table}}");
                builder.HasKey(aggregate => aggregate.Id);
                builder.Property(aggregate => aggregate.Id).ValueGeneratedNever();
                builder.Property<byte[]>("Version").IsRowVersion();
                builder.Ignore(aggregate => aggregate.DomainEvents);
            }
        }

        """;

    /// <summary>The repository implementation.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <returns>The file content.</returns>
    public static string Repository(string service, string feature, string aggregate) => $$"""
        using Microsoft.EntityFrameworkCore;
        using {{service}}.Domain.{{feature}};

        namespace {{service}}.Infrastructure.Persistence.Write.Repositories;

        /// <summary>Implements <see cref="I{{aggregate}}Repository"/> on <see cref="{{service}}WriteDbContext"/>.</summary>
        /// <param name="context">The write context of the command's unit of work.</param>
        internal sealed class {{aggregate}}Repository({{service}}WriteDbContext context) : I{{aggregate}}Repository
        {
            /// <inheritdoc />
            public Task<{{aggregate}}?> FindAsync({{aggregate}}Id id, CancellationToken cancellationToken) =>
                context.Set<{{aggregate}}>().FirstOrDefaultAsync(aggregate => aggregate.Id == id, cancellationToken);

            /// <inheritdoc />
            public void Add({{aggregate}} aggregate) => context.Add(aggregate);

            /// <inheritdoc />
            public void Remove({{aggregate}} aggregate) => context.Remove(aggregate);
        }

        """;

    /// <summary>A first domain test of the aggregate (no mocks).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <returns>The file content.</returns>
    public static string DomainTest(string service, string feature, string aggregate) => $$"""
        using {{service}}.Domain.{{feature}};
        using SuperApp.Framework.Testing;

        namespace {{service}}.Domain.Tests;

        public sealed class {{aggregate}}Tests
        {
            private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            [Fact]
            public void Create_gives_a_new_identifier_and_the_creation_time()
            {
                var created = ResultAssert.Success({{aggregate}}.Create(Now));

                Assert.NotEqual(Guid.Empty, created.Id.Value);
                Assert.Equal(Now, created.CreatedAt);
            }
        }

        """;

    /// <summary>A domain event raised by an aggregate (ADR-0027).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder of the aggregate.</param>
    /// <param name="aggregate">Aggregate that raises the event.</param>
    /// <param name="name">Event name in the past tense, e.g. <c>InvoiceIssued</c>.</param>
    /// <returns>The file content.</returns>
    public static string DomainEvent(string service, string feature, string aggregate, string name) => $$"""
        using SuperApp.Framework.Domain.Events;

        namespace {{service}}.Domain.{{feature}}.Events;

        /// <summary>Domain event: TODO what happened to the <see cref="{{aggregate}}"/>, in the past tense of the domain language.</summary>
        /// <remarks>
        /// Raised by <see cref="{{aggregate}}"/> with <c>Raise(...)</c> (TODO: in which method) and dispatched in the same transaction when the
        /// unit of work saves (ADR-0027). Internal to the service: it may change freely; other contexts see only integration events.
        /// </remarks>
        /// <param name="{{aggregate}}Id">The aggregate the event happened to.</param>
        /// <param name="OccurredAt">When it happened (UTC, from <c>IClock</c>).</param>
        public sealed record {{name}}({{aggregate}}Id {{aggregate}}Id, DateTimeOffset OccurredAt) : IDomainEvent;

        """;

    /// <summary>An integration event of the published language (ADR-0005, ADR-0027).</summary>
    /// <param name="service">Service name.</param>
    /// <param name="aggregate">Aggregate the event is about.</param>
    /// <param name="name">Contract name with the version, e.g. <c>InvoiceIssuedV1</c>.</param>
    /// <returns>The file content.</returns>
    public static string IntegrationEvent(string service, string aggregate, string name) => $$"""
        namespace {{service}}.Contracts;

        // Published language of the {{service}} service (ADR-0005): primitive types only; a breaking change means a new version of the type.

        /// <summary>Integration event: TODO what happened, for the other contexts that react to it.</summary>
        /// <remarks>
        /// Published through the outbox in the transaction of the change (ADR-0005). Changes are backward compatible only (new optional
        /// fields); anything else is a new version of the type. Consumers must be idempotent.
        /// </remarks>
        /// <param name="{{aggregate}}Id">Identifier of the {{aggregate}}.</param>
        /// <param name="OccurredAt">When it happened (UTC).</param>
        public sealed record {{name}}(Guid {{aggregate}}Id, DateTimeOffset OccurredAt);

        """;

    /// <summary>The domain event handler that publishes the integration event.</summary>
    /// <param name="service">Service name.</param>
    /// <param name="feature">Domain folder of the aggregate.</param>
    /// <param name="aggregate">Aggregate name.</param>
    /// <param name="domainEvent">Domain event name.</param>
    /// <param name="contract">Integration event name.</param>
    /// <returns>The file content.</returns>
    public static string Translator(string service, string feature, string aggregate, string domainEvent, string contract) => $$"""
        using SuperApp.Framework.Application.Events;
        using {{service}}.Contracts;
        using {{service}}.Domain.{{feature}}.Events;

        namespace {{service}}.Application.IntegrationEvents;

        /// <summary>Publishes <see cref="{{contract}}"/> when <see cref="{{domainEvent}}"/> happens (ADR-0027).</summary>
        /// <remarks>
        /// Runs inside the unit of work: the message goes to the outbox in the same transaction as the change, so it is sent only if the
        /// change is committed (ADR-0005). Only the data other contexts need is published.
        /// </remarks>
        /// <param name="publisher">Port of the integration event outbox.</param>
        internal sealed class {{domainEvent}}Translator(IIntegrationEventPublisher publisher) : IDomainEventHandler<{{domainEvent}}>
        {
            /// <inheritdoc />
            public Task HandleAsync({{domainEvent}} domainEvent, CancellationToken cancellationToken) =>
                publisher.PublishAsync(new {{contract}}(domainEvent.{{aggregate}}Id.Value, domainEvent.OccurredAt), cancellationToken);
        }

        """;

    /// <summary>A consumer of an integration event in the Worker of a service (ADR-0005).</summary>
    /// <param name="service">Service that consumes.</param>
    /// <param name="publisher">Service whose contract is consumed.</param>
    /// <param name="contract">Integration event type, e.g. <c>InvoiceIssuedV1</c>.</param>
    /// <param name="consumer">Consumer class name.</param>
    /// <returns>The file content.</returns>
    public static string Consumer(string service, string publisher, string contract, string consumer) => $$"""
        using MassTransit;
        using {{publisher}}.Contracts;

        namespace {{service}}.Worker.Consumers;

        /// <summary>Consumes <see cref="{{contract}}"/>: TODO what the {{service}} service does when it happens.</summary>
        /// <remarks>
        /// <para>
        /// Thin: TODO inject <c>ISender</c> and send one command of the Application layer; the business decision belongs to the aggregate
        /// (ADR-0005). A failed <c>Result</c> is a business rejection: log it with a <c>[LoggerMessage]</c> warning (event ID from the
        /// service range) and acknowledge the message; a technical exception is retried and ends in the <c>_error</c> queue.
        /// </para>
        /// <para>Idempotency: the EF inbox skips a redelivered message; the command must also tolerate the same event twice.</para>
        /// </remarks>
        public sealed class {{consumer}} : IConsumer<{{contract}}>
        {
            /// <summary>Handles one delivery of the event.</summary>
            /// <param name="context">The consumed message; its cancellation token goes to the command.</param>
            /// <returns>A task that completes when the command has finished.</returns>
            public Task Consume(ConsumeContext<{{contract}}> context) =>
                throw new NotImplementedException("TODO: send the command for {{contract}} (recipe 05).");
        }

        """;
}
