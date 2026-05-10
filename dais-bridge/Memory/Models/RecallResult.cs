namespace Darbee.Gateway.Memory.Models;

public record RecallResult(
    IReadOnlyList<ScoredMemoryItem> Items,
    IReadOnlyList<string> ExtractedEntityIds);
