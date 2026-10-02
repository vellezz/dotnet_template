namespace SuperApp.Framework.Infrastructure.Messaging;

/// <summary>
/// Role of a process in delivering the transactional outbox: every process writes to the outbox, but only one kind of process sends its content
/// to RabbitMQ (ADR-0005).
/// </summary>
/// <remarks>
/// APIs write messages but leave delivery to the Worker, so a scaled-out API does not multiply delivery work and request latency does not
/// depend on the broker. Delivery restarts automatically after a Worker restart, because pending messages stay in the database.
/// </remarks>
public enum OutboxDelivery
{
    /// <summary>The process stores outgoing messages in the outbox but does not send them; used by API processes.</summary>
    Disabled,

    /// <summary>The process also runs the delivery service that sends committed outbox messages to RabbitMQ; used by the Worker.</summary>
    Enabled,
}
