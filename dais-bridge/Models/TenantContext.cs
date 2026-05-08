namespace Darbee.Gateway.Models;

public class TenantContext
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string Configuration { get; set; } = string.Empty;
}
