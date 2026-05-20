using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// Strategy pattern — entity extraction abstraction.
/// Implementations: SubstringEntityExtractor (current), NerEntityExtractor (future).
/// </summary>
public interface IEntityExtractor
{
    Task<IReadOnlyList<string>> ExtractAsync(TenantId tenantId, string query, CancellationToken ct = default);
}
