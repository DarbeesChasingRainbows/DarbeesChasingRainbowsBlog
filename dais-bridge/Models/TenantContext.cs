namespace Darbee.Gateway.Models;

public sealed class TenantContext
{
    public string TenantId { get; init; } = string.Empty;
    public string TenantName { get; init; } = string.Empty;
    public string Configuration { get; init; } = string.Empty;

    public static TenantContext Admin { get; } = new() { TenantId = "admin", TenantName = "Admin" };
    public static TenantContext ForKid(string kidId, string? displayName = null) =>
        new() { TenantId = $"kid:{kidId}", TenantName = displayName ?? kidId };
}
