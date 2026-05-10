namespace Darbee.Gateway.Memory.Models;

public record MemoryEdge(
    string Key,
    string From,
    string To,
    string Kind,
    double Weight,
    string TenantId,
    DateTime CreatedAt);
