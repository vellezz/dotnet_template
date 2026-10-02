---
applyTo: "src/Services/**/*.Worker/**/*.cs,src/Services/**/*.Contracts/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/ServiceName.Worker/**/*.cs,src/Tools/SuperApp.Cli/Templates/superapp-service/ServiceName.Contracts/**/*.cs,src/Analytics/SuperApp.AnalyticsForwarder/**/*.cs"
---

# Worker (consumers) and Contracts (integration events)

## Contracts

- Integration events are the public, versioned language of a context: `public sealed record {Fact}V{n}(...)` with primitives only
  (`Guid`, `string`, `int`, `DateTimeOffset`), only the data other contexts need. No domain types, no references to Domain.
- Changes must be backward compatible (add fields at the end); a breaking change is a new type `...V{n+1}` published alongside the old one.
- Document when the event is published (and when not), delivery semantics (outbox, at least once, consumers idempotent) and every field.
- Contracts are also consumed by `SuperApp.AnalyticsForwarder`; a field added to a contract never reaches PostHog by itself, because the
  forwarder's consumer decides on an explicit property allow-list.

## Service Worker consumers

- `Consumers/{Event}Consumer.cs`, `public sealed partial class`, `IConsumer<TEvent>`: translate the message into one command of this
  service and `await sender.Send(command, context.CancellationToken)`. No business logic, no direct database access.
- A failed `Result` is a business rejection: log it with a `[LoggerMessage]` warning (EventId from the Worker range) and return
  (acknowledged); `TransactionBehavior` has already discarded the command's tracked changes, so nothing of it is saved with the inbox.
  Technical exceptions, including `DbUpdateConcurrencyException`, propagate (retry 100 ms, 500 ms, 1 s, 5 s, then the `_error` queue).
- Commands run as the system identity (all scopes, no subject); make them safe to run twice even though the inbox deduplicates.
- Consumers are registered automatically (`bus.AddConsumers(typeof(Program).Assembly)`); queue names are kebab-case with the service prefix.
- Service Workers never send analytics events and never use the PostHog SDK; product analytics is the forwarder's job (below).

## Analytics forwarder (`src/Analytics/SuperApp.AnalyticsForwarder`, ADR-0036)

- The forwarder is the one justified exception to "a consumer delegates to a command": it has no commands, database, domain or
  inbox and only maps an integration event to a PostHog product event. It depends on services only through their `{Service}.Contracts`
  (architecture rule 10). Messaging is `AddAppEventSubscriber(configuration, "analytics", ...)`: no outbox/inbox, retry 100 ms,
  500 ms, 1 s, 5 s, quorum queues `analytics-{event}`.
- A new backend product event:
  1. a constant in `Events/ProductEventNames.cs` named `{service}_{object}_{past-tense verb}` in snake case
     (`knowledge_material_published`); names are a contract with analysts: add new ones, never rename existing ones;
  2. `Consumers/{Event}Consumer.cs`, `public sealed class`, `IConsumer<{Event}V{n}>`, calling `IProductEventSink.Capture(new ProductEvent(name,
     occurredAt from the message, context.MessageId, subject, properties))` and returning; no other logic;
  3. `Subject` is the user's `sub` from the message when the fact belongs to a user (the sink turns it into the pseudonym `u_…`),
     or `null` for system facts (sent as `system` without a person profile);
  4. properties are an explicit allow-list built in the consumer: names, identifiers of catalog objects and categories only, keys in
     snake case. Never copy the whole message; never send titles, user-entered text, e-mail or sleep diary data (dates, durations,
     ratings: health data). Every new property goes through a privacy review;
  5. a test in `SuperApp.AnalyticsForwarder.Tests` asserting the name, subject and the exact set of property keys;
  6. the new queue in `deploy/helm/superapp-analytics-forwarder/values.yaml` (`keda.queues`).
- Delivery is best effort (in-memory client queue, flushed on shutdown); a redelivered message may repeat an analytics event. Never
  make business behavior depend on analytics. Log EventIds of the forwarder are in the 9000–9999 range (`docs/logowanie-eventid.md`).
