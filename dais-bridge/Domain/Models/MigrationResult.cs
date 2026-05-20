namespace Darbee.Gateway.Domain.Models;

public sealed record MigrationResult(
    EmbeddingConfig? Previous,
    EmbeddingConfig Current,
    IReadOnlyList<string> IndexesDropped,
    IReadOnlyDictionary<string, int> DocsMarkedForReembed,
    int QueueSizeAfter);
