namespace Darbee.Gateway.Domain.Events;

/// <summary>
/// Raised after stale post documents are deleted from the repository.
/// </summary>
public sealed record StalePostsDeletedEvent(
    int Count,
    IReadOnlyCollection<string> ScopedCollections,
    DateTime OccurredAt);