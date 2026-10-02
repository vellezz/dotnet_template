---
applyTo: "src/Services/**/*.Application/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/ServiceName.Application/**/*.cs"
---

# Application layer

- No references to Infrastructure, EF Core or MassTransit. Ports (interfaces) are defined here or in `SuperApp.Framework.Application`.
- Vertical slices: `Features/{Aggregate}/{UseCase}/` with one file per type: `{UseCase}.cs` (command or query record, no "Command"
  suffix), `{UseCase}Validator.cs`, `{UseCase}Handler.cs` (commands only; query handlers live in Infrastructure), DTO records.
- Commands: `public sealed record X(...) : ICommand` or `ICommand<Result<T>>`; queries: `IQuery<Result<T>>`. Inputs and outputs are
  primitives (`Guid`, `string`, `int`, enums), never domain types. Every command/query has `[RequiresScope({Service}Scopes.X)]`
  (scope constants in `{Service}Scopes`), or `[AllowAnonymousRequest]` for intentionally public operations.
- Validators (`internal sealed`, `AbstractValidator<T>`) check input shape with the domain constants and the same trimming as the
  aggregate; business rules stay in the aggregate.
- Command handlers (`internal sealed`, `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResponse>`):
  1. convert IDs and values with `Xxx.Create(...).TryGetValue(out var x, out var error)` (an invalid ID in a path is NotFound),
  2. load through the repository, 3. call one aggregate method or factory, 4. `repository.Add(...)` for new aggregates,
  5. return the aggregate's `Result`. Never call `SaveChangesAsync`, never open transactions, never modify a second aggregate,
  never publish integration events directly, never throw for expected failures.
- Current user: `ICurrentUser` (`Subject`, `HasScope`); resource-level rules return NotFound/Forbidden errors. The owner always
  comes from `ICurrentUser.Subject`, never from a user ID in the request payload, because callers inside the cluster (our BFF,
  another experience's BFF, our services) forward the user's token (ADR-0040).
- Synchronous calls to other components go through a port defined here (e.g. `I{Foreign}Gateway`) and implemented in Infrastructure.
  Port methods take the concepts of this context, not a user identifier as the basis for access: in the user's context the token is
  forwarded and the receiver applies its own rules. Prefer integration events; a call that needs data of another experience goes to
  that experience's BFF internal API, never to its domain services.
- Feature flags (ADR-0036): declare each flag once in `{Service}FeatureFlags.cs` next to `{Service}Scopes.cs`, as
  `public static readonly FeatureFlag MaterialRatings = new("knowledge_material_ratings", false);` (key in snake case with the
  service prefix, documented: what it switches, who owns it, when it will be removed). Inject the port `IFeatureFlags`
  (`SuperApp.Framework.Application.FeatureFlags`) into the handler and call `await featureFlags.IsEnabledAsync(...FeatureFlags.X, cancellationToken)`.
  - The default value is what runs when PostHog is slow, unavailable or does not know the flag, so it must be safe permanently
    (usually the old behavior). Values are stable within one request or message.
  - A flag is not a permission: keep `[RequiresScope]` and resource rules; a flag only switches server behavior, and that check
    always happens in the backend (a flag in the UI is cosmetic).
  - Business rules stay in the aggregate: pass the flag's outcome as a parameter to the aggregate method instead of reading flags
    in the domain.
  - Never reference the PostHog SDK, send analytics events or compute analytics identifiers in a service (architecture rule 11);
    backend product events come from `SuperApp.AnalyticsForwarder` (see `worker-contracts.instructions.md`).
- Application tests cover both flag values with a fake `IFeatureFlags` in `Fakes/` (one type per file; add it when the first flag is used).
- Domain → integration event translators: `IntegrationEvents/{Event}Translator.cs`, `internal sealed`,
  `IDomainEventHandler<TDomainEvent>` calling `IIntegrationEventPublisher.PublishAsync`, timestamps from the domain event.
- Tests: `{Service}.Application.Tests` with fakes from `Fakes/` (pipeline, handlers, validators, translators).
