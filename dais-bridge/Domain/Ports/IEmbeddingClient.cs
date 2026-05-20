namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// Strategy pattern — embedding provider abstraction.
/// Swap OpenAI-compatible for any provider without changing domain logic.
/// </summary>
public interface IEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    int Dimension { get; }
}
