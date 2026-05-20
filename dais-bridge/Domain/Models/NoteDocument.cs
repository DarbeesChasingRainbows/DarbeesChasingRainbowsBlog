using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Models;

public sealed record NoteDocument(
    string Key,
    string Title,
    string Text,
    MemoryKind Kind,
    TenantId TenantId,
    IReadOnlyDictionary<string, object>? Metadata = null);
