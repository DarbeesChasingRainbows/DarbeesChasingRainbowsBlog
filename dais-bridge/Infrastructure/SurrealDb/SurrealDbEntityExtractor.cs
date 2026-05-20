using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Infrastructure.SurrealDb;

/// <summary>
/// SurrealDB-specific implementation of entity extraction.
/// </summary>
public sealed class SurrealDbEntityExtractor : IEntityExtractor
{
    private readonly IMemoryRepository _repository;

    public SurrealDbEntityExtractor(IMemoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<string>> ExtractAsync(
        TenantId tenantId, string query, CancellationToken ct = default)
    {
        // SurrealQL substring + alias match on memory_entities for the tenant.
        // Lowercases both sides so matching is case-insensitive.
        var sql = @"
SELECT VALUE id FROM memory_entities
WHERE tenant_id = $tenant
  AND (
    string::contains(string::lowercase($query), string::lowercase(canonical_name))
    OR (
      aliases IS NOT NONE AND
      array::any(aliases, |$a| string::contains(string::lowercase($query), string::lowercase($a)))
    )
  );";

        var bindVars = new Dictionary<string, object>
        {
            ["tenant"] = tenantId.Value,
            ["query"] = query,
        };

        return await _repository.QueryAsync<string>(sql, bindVars, ct);
    }
}