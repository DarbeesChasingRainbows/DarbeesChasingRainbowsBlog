using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// SRP: Migration is a separate concern from document CRUD.
/// Consumers: admin /api/admin/migrate-embeddings endpoint only.
/// </summary>
public interface IEmbeddingMigrator
{
    Task<MigrationResult> MigrateEmbeddingsAsync(string confirmToken, CancellationToken ct = default);
    Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default);
}
