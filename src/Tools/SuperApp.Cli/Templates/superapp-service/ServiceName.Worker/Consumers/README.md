# Consumers

This folder holds the MassTransit consumers of integration events (from the `{Service}.Contracts` packages of other services, or of this service itself).
Every consumer found in the Worker assembly is registered automatically (`bus.AddConsumers(typeof(Program).Assembly)` in `Program.cs`).

Rules (ADR-0005):

- A consumer is thin: it maps the message to a command of this service and sends it through `ISender`. Business logic belongs to the
  aggregate, never to the consumer.
- Types from the foreign `Contracts` package stay in the consumer; the command uses this service's own language.
- The inbox skips a redelivered message with the same message ID, and events the command publishes go through the outbox; both are
  configured by `SuperApp.Framework` (`AddAppMessaging`). Still make the command safe to run twice where possible.
- Commands run as the Worker's system identity (`AddAppWorker`), which has every scope.
- A business rejection (failed `Result`) is logged with a `[LoggerMessage]` warning and the message is acknowledged, because a retry
  would not change the outcome. Technical exceptions propagate: the endpoint retry policy applies and, when retries are exhausted,
  the message goes to the `_error` queue.
- Example: `Knowledge.Worker/Consumers/MaterialArchivedConsumer.cs`.
