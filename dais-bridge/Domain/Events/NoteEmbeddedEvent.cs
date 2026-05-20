using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Events;

/// <summary>
/// Raised after a note document is successfully embedded and persisted.
/// </summary>
public sealed record NoteEmbeddedEvent(
    string Key,
    string Kind,
    TenantId TenantId,
    DateTime OccurredAt);
