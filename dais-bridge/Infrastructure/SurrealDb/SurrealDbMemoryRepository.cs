using System.Text.Json;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using SurrealDb.Net;

namespace Darbee.Gateway.Infrastructure.SurrealDb;

/// <summary>
/// SurrealDB adapter for the memory persistence ports.
/// Implements <see cref="IMemoryRepository"/>, <see cref="ISchemaManager"/>, <see cref="IEmbeddingMigrator"/>.
/// One concrete class behind multiple ports is fine per ISP — consumers depend only on the interface they need.
/// </summary>
public sealed class SurrealDbMemoryRepository : IMemoryRepository, ISchemaManager, IEmbeddingMigrator, IDisposable
{
    private readonly ISurrealDbClient _surreal;
    private readonly string _embeddingModelId;
    private readonly int _embeddingDimension;
    private readonly IEmbeddingClient _embeddings;

    public SurrealDbMemoryRepository(
        ISurrealDbClient surreal,
        string embeddingModelId,
        int embeddingDimension,
        IEmbeddingClient embeddings)
    {
        _surreal = surreal;
        _embeddingModelId = embeddingModelId;
        _embeddingDimension = embeddingDimension;
        _embeddings = embeddings;
    }

    // ----- ISchemaManager -----
    public Task EnsureSchemaAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T5: SurrealDB schema bootstrap.");
    public Task EnsureSchemaIfNeededAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T5: SurrealDB schema bootstrap.");
    public Task EnsureVectorIndexAsync(string collection, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T5: SurrealDB vector index bootstrap.");
    public Task<bool> IsVectorIndexReadyAsync(string collection, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T5: SurrealDB vector index readiness.");
    public Task<List<string>> ListCollectionsAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T5: SurrealDB collection listing.");

    // ----- IMemoryRepository: writes -----
    public Task<UpsertPostResult> UpsertPostAsync(PostDocument post, bool force, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T6: UpsertPost.");
    public Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T6: UpsertNote.");
    public Task<WriteResult> UpsertDecisionAsync(string tenantId, string subject, string chose, string because, IReadOnlyList<string> alternatives, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertDecision.");
    public Task<WriteResult> UpsertObservationAsync(string tenantId, string source, string text, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertObservation.");
    public Task<WriteResult> UpsertFactAsync(string tenantId, string text, string? sourceThread, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertFact.");
    public Task<WriteResult> UpsertSummaryAsync(string tenantId, string text, string threadId, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertSummary.");
    public Task<string> UpsertEntityAsync(string tenantId, string canonicalName, IReadOnlyList<string> aliases, string type, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertEntity.");
    public Task<string> UpsertEdgeAsync(string tenantId, string fromId, string toId, string kind, double weight, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: UpsertEdge.");

    // ----- IMemoryRepository: reads -----
    public Task<List<PostSearchHit>> SearchAsync(float[] queryVec, IReadOnlyList<MemoryKind> kinds, IReadOnlyList<string> tenants, int k, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T7: Search with KNN.");
    public Task<string?> ReadPostHashAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ReadPostHash.");
    public Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ReadNoteDocument.");
    public Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(int limit = 100, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T9: ListPendingEmbeddings.");

    // ----- IMemoryRepository: deletes -----
    public Task<int> DeleteStalePostsAsync(IReadOnlyCollection<(string Collection, string Slug)> currentPosts, IReadOnlyCollection<string>? scopedCollections = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T8: DeleteStalePosts.");
    public Task<int> DeleteStaleNotesAsync(IReadOnlyList<string> currentKeys, string tenant, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T8: DeleteStaleNotes.");

    // ----- IMemoryRepository: query -----
    public Task<List<T>> QueryAsync<T>(string query, Dictionary<string, object> bindVars, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T11: raw QueryAsync for recall engine.");

    // ----- IEmbeddingMigrator -----
    public Task<MigrationResult> MigrateEmbeddingsAsync(string confirmToken, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T10: MigrateEmbeddings.");
    public Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2 T10: ReadEmbeddingConfig.");

    public void Dispose()
    {
        // ISurrealDbClient is managed by DI; nothing to dispose here.
    }
}
