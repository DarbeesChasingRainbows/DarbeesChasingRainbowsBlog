using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// Hybrid recall domain service port.
/// Combines graph expansion and vector top-K search.
/// Consumers: MemoryPlugin.Recall.
/// </summary>
public interface IRecallEngine
{
    Task<RecallResult> RecallAsync(TenantId tenantId, string query, int topK = 8, int expandHops = 1, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ExtractEntitiesAsync(TenantId tenantId, string query, CancellationToken ct = default);
}
