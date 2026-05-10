namespace Darbee.Gateway.Memory.Models;

public record MemoryEdge(
    string Key,
    string From,              // collection/key form: "memory_decisions/abc123"
    string To,
    string Kind,              // "mentions" | "depends-on" | "supersedes" | "tagged" | "about-file"
    double Weight,
    string TenantId,
    DateTime CreatedAt);
