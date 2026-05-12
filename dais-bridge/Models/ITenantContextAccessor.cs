namespace Darbee.Gateway.Models;

public interface ITenantContextAccessor
{
    TenantContext? Current { get; set; }
    TenantContext Required => Current
        ?? throw new InvalidOperationException("Tenant context not set on this call.");
}
