---
applyTo: "src/Services/**/*.Domain/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/ServiceName.Domain/**/*.cs"
---

# Domain layer

- No dependencies except `SuperApp.Framework.Domain` and the BCL: no EF Core, MediatR, MassTransit, ASP.NET, Refit, no attributes for persistence.
- Aggregates derive from `AggregateRoot<TId>`; one folder per aggregate (`{Aggregate}/`) with its strongly typed ID, `{Aggregate}Errors`,
  `I{Aggregate}Repository` and `Events/`. `Common/` only for concepts shared by several aggregates of the context.
- Aggregate shape: private constructor taking the ID (also used by EF), static factory `Create(...)`/domain verb returning `Result<T>`,
  properties with private setters, collections as private fields exposed as `IReadOnlyList<T>`, limits as `public const` members.
- State changes only through methods named in the ubiquitous language (`Publish`, `Archive`, `Rename`), which check invariants, change
  state, call `Raise(new SomethingHappened(...))` and return `Result`. Make repeated calls idempotent (no change, no event).
- Pass time in as a parameter (`DateTimeOffset now`); never read the clock in the domain.
- Errors: `static readonly Error` fields in `{Aggregate}Errors`, codes `{service}.{concept}.{problem}`, error type by caller impact.
- Strongly typed IDs: `readonly record struct XxxId : IStronglyTypedId<XxxId, Guid>` with `New()` (`Guid.CreateVersion7()`),
  `Create(Guid)` rejecting `Guid.Empty`, `FromTrusted`, `ToString()`. Value objects: `readonly record struct` implementing
  `ISingleValueObject<TSelf, TValue>` (single value) or `sealed record` with a validating factory (several values).
- Domain events: `sealed record` in `{Aggregate}/Events/`, past tense, implementing `IDomainEvent`, carrying the aggregate ID, the
  changed data and the time of the change.
- Repository interfaces: one per aggregate, no generic repository, no `IQueryable`; reads async with `CancellationToken`,
  `Add`/`Remove` synchronous.
- References between aggregates by ID only; no navigation properties to other aggregates.
- Tests: `{Service}.Domain.Tests`, plain unit tests without mocks, asserting `Result` and error codes.
