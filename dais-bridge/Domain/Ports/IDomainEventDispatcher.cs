namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// Observer pattern — lightweight domain event dispatch.
/// No external message bus; handlers are wired in DI.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class;
}

/// <summary>
/// Observer pattern — subscribes to a specific domain event type.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
