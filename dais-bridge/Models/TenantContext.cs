using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Models;

public sealed class TenantContext
{
    public TenantId TenantId { get; init; } = new("public");
    public string TenantName { get; init; } = string.Empty;
    public string Configuration { get; init; } = string.Empty;

    public static TenantContext Admin { get; } = new() { TenantId = new("admin"), TenantName = "Admin" };
    public static TenantContext ForKid(string kidId, string? displayName = null) =>
        new() { TenantId = new($"kid:{kidId}"), TenantName = displayName ?? kidId };
}
