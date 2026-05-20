using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Domain.Services;

/// <summary>
/// In-process Observer pattern implementation.
/// Dispatches domain events to all registered handlers via DI.
/// No external message bus — suitable for current scale.
/// </summary>
public sealed class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _services;

    public InMemoryDomainEventDispatcher(IServiceProvider services)
    {
        _services = services;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        var handlers = _services.GetServices<IDomainEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(@event, ct);
        }
    }
}

file static class ServiceProviderExtensions
{
    public static IEnumerable<T> GetServices<T>(this IServiceProvider provider)
    {
        return (IEnumerable<T>?)provider.GetService(typeof(IEnumerable<T>))
            ?? Enumerable.Empty<T>();
    }
}
