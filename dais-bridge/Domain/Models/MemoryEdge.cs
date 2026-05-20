using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Models;

public record MemoryEdge(
    string Key,
    string From,
    string To,
    string Kind,
    double Weight,
    TenantId TenantId,
    DateTime CreatedAt);
