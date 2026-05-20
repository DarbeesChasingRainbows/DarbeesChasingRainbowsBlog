using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Domain.Services;

/// <summary>
/// Null Object pattern — replaces nullable IEmbeddingClient.
/// Throws descriptive error on use instead of requiring null checks everywhere.
/// </summary>
public sealed class NullEmbeddingClient : IEmbeddingClient
{
    public int Dimension => 0;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Embedding client not configured. Set EmbeddingApi:BaseUrl in appsettings.json or environment variables.");

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Embedding client not configured. Set EmbeddingApi:BaseUrl in appsettings.json or environment variables.");
}
