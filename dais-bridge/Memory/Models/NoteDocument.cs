namespace Darbee.Gateway.Memory.Models;

public sealed record NoteDocument(
    string Key,
    string Title,
    string Text,
    MemoryKind Kind,
    string TenantId,
    IReadOnlyDictionary<string, object>? Metadata = null);
