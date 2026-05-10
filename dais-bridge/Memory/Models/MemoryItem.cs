namespace Darbee.Gateway.Memory.Models;

public record MemoryItem(
    string Key,
    MemoryKind Kind,
    string Text,
    float[]? Embedding,
    string TenantId,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, object>? Metadata = null);
