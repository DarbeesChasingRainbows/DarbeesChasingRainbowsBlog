namespace Darbee.Gateway.Memory.Models;

public record ScoredMemoryItem(
    MemoryItem Item,
    double Cosine,
    double Proximity,
    double Score,
    int? HopsFromQuery,
    IReadOnlyList<string> PathEntityKeys);
