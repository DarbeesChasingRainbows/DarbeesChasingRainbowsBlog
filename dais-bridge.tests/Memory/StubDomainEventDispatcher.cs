using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Tests.Memory;

public sealed class StubDomainEventDispatcher : IDomainEventDispatcher
{
    public List<object> DispatchedEvents { get; } = new();

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        DispatchedEvents.Add(@event);
        return Task.CompletedTask;
    }
}