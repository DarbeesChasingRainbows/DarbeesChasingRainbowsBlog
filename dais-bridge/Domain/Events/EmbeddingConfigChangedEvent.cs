using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Domain.Events;

/// <summary>
/// Raised after embedding configuration is migrated (model or dimension change).
/// </summary>
public sealed record EmbeddingConfigChangedEvent(
    EmbeddingConfig? Previous,
    EmbeddingConfig Current,
    int DocsMarkedForReembed,
    DateTime OccurredAt);
