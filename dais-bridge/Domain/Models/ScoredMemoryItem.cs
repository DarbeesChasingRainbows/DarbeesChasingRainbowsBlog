namespace Darbee.Gateway.Domain.Models;

public record ScoredMemoryItem(
    MemoryItem Item,
    double Cosine,
    double Proximity,
    double Score,
    int? HopsFromQuery,
    IReadOnlyList<string> PathEntityKeys);
