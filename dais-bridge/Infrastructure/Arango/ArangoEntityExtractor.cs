using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Infrastructure.Arango;

/// <summary>
/// Strategy pattern — current entity extraction implementation.
/// Delegates to IMemoryRepository.QueryAsync for substring/alias matching.
/// </summary>
public sealed class ArangoEntityExtractor : IEntityExtractor
{
    private readonly IMemoryRepository _repository;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _nerFallback;

    public ArangoEntityExtractor(
        IMemoryRepository repository,
        Func<string, Task<IReadOnlyList<string>>>? nerFallback = null)
    {
        _repository = repository;
        _nerFallback = nerFallback;
    }

    public async Task<IReadOnlyList<string>> ExtractAsync(
        TenantId tenantId, string query, CancellationToken ct = default)
    {
        // Substring + alias matching via the repository's query method.
        // The actual query is backend-specific (AQL or SurrealQL),
        // so we delegate to the repository's QueryAsync<T>.
        // This will be wired by the concrete adapter.
        var aql = @"FOR e IN @@col
                      FILTER e.tenant_id == @tenantId
                      LET hit = CONTAINS(LOWER(@query), LOWER(e.canonical_name))
                                OR LENGTH(FOR a IN (e.aliases != null ? e.aliases : [])
                                            FILTER CONTAINS(LOWER(@query), LOWER(a))
                                            RETURN 1) > 0
                      FILTER hit
                      RETURN e._id";
        var bindVars = new Dictionary<string, object>
        {
            ["@col"] = Darbee.Gateway.Domain.Models.MemoryCollections.Entities,
            ["tenantId"] = tenantId.Value,
            ["query"] = query,
        };

        var results = await _repository.QueryAsync<string>(aql, bindVars, ct);
        if (results.Count > 0) return results;

        if (_nerFallback is not null)
            return await _nerFallback(query);

        return Array.Empty<string>();
    }
}
