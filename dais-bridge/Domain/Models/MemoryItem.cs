using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Models;

public record MemoryItem(
    string Key,
    MemoryKind Kind,
    string Text,
    float[]? Embedding,
    TenantId TenantId,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, object>? Metadata = null);
