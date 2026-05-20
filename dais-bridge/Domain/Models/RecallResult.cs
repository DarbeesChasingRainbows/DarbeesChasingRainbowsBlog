namespace Darbee.Gateway.Domain.Models;

public record RecallResult(
    IReadOnlyList<ScoredMemoryItem> Items,
    IReadOnlyList<string> ExtractedEntityIds);
