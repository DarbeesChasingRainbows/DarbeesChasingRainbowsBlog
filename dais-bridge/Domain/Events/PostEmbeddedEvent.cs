using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Events;

/// <summary>
/// Raised after a post document is successfully embedded and persisted.
/// </summary>
public sealed record PostEmbeddedEvent(
    string Slug,
    string Collection,
    TenantId TenantId,
    DateTime OccurredAt);
