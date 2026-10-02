---
mode: agent
description: Add a backend product analytics event (PostHog) to the analytics forwarder, following ADR-0036
---

Add a new backend product event sent to PostHog. Ask for missing details: which fact (integration event and service), why the
product team needs it, whether it belongs to a user or is a system fact, and which properties are wanted.

Start with the repository tool: `dotnet superapp add product-event {Contract}V1` creates the forwarder consumer (identifiers, time and
user only), the name in `ProductEventNames` and the Contracts reference; review the properties against the rules below.

Rules (ADR-0036, `docs/przewodnik/21-analityka-i-feature-flags.md`, `.github/instructions/worker-contracts.instructions.md`):

- Product events are produced only by `src/Analytics/SuperApp.AnalyticsForwarder` from an existing integration event in `{Service}.Contracts`.
  Do not touch the domain service: no PostHog SDK, no analytics code, no new command. If no integration event carries the fact, stop and
  propose adding one to the service first (domain event → translator → `{Fact}V{n}` through the outbox).
- Before writing code, list every proposed property with its value type and state why it is allowed. Allowed: names, identifiers of
  catalog objects, categories. Never: titles, user-entered text, e-mail, `sub`, sleep diary data (dates, durations, ratings are health
  data). Flag the list for a privacy review in the summary.

Steps:

1. `Events/ProductEventNames.cs`: a constant `{service}_{object}_{past-tense verb}` in snake case with complete XML docs (when it is
   sent, subject, properties). Never rename existing names.
2. `Consumers/{Event}Consumer.cs`: `public sealed class`, `IConsumer<{Event}V{n}>`, one `IProductEventSink.Capture(new ProductEvent(...))`
   with the time of the fact from the message, `context.MessageId`, the user's `sub` as subject (or `null` for system facts) and an
   explicit property dictionary with snake case keys. No other logic. Follow `MaterialPublishedConsumer`.
3. If the integration event comes from a service the forwarder does not reference yet, add a project reference to that service's
   `{Service}.Contracts` only (architecture rule 10).
4. Test in `SuperApp.AnalyticsForwarder.Tests/ConsumerMappingTests.cs` on the MassTransit test harness: event name, subject and the exact
   set of property keys.
5. Add the queue `analytics-{event}` to `keda.queues` in `deploy/helm/superapp-analytics-forwarder/values.yaml`.
6. Update `docs/przewodnik/21-analityka-i-feature-flags.md` (event catalog) and `docs/architektura.md` §5.6 if the catalog is listed there.
7. Run `dotnet build SuperApp.slnx` and `dotnet test --solution SuperApp.slnx` (including architecture tests); summarize the new event and its
   properties for the privacy review.
