using System.Text.Json;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Ports;

/// <summary>
/// Repository pattern (Fowler PoEAA Ch. 10) — document persistence port.
/// Mediates between domain and data mapping layers.
/// Consumers: ContentRagEndpoints, MemoryPlugin, IRecallEngine adapters.
/// </summary>
public interface IMemoryRepository : IDisposable
{
    // Write
    Task<UpsertPostResult> UpsertPostAsync(PostDocument post, bool force, CancellationToken ct = default);
    Task<UpsertNoteResult> UpsertNoteAsync(NoteDocument note, CancellationToken ct = default);
    Task<WriteResult> UpsertDecisionAsync(TenantId tenantId, string subject, string chose, string because, IReadOnlyList<string> alternatives, CancellationToken ct = default);
    Task<WriteResult> UpsertObservationAsync(TenantId tenantId, string source, string text, object payload, CancellationToken ct = default);
    Task<WriteResult> UpsertFactAsync(TenantId tenantId, string text, string? sourceThread, CancellationToken ct = default);
    Task<WriteResult> UpsertSummaryAsync(TenantId tenantId, string text, string threadId, CancellationToken ct = default);
    Task<string> UpsertEntityAsync(TenantId tenantId, string canonicalName, IReadOnlyList<string> aliases, string type, CancellationToken ct = default);
    Task<string> UpsertEdgeAsync(TenantId tenantId, string fromId, string toId, string kind, double weight, CancellationToken ct = default);

    // Read
    Task<List<PostSearchHit>> SearchAsync(float[] queryVec, IReadOnlyList<MemoryKind> kinds, IReadOnlyList<TenantId> tenants, int k, CancellationToken ct = default);
    Task<string?> ReadPostHashAsync(string key, CancellationToken ct = default);
    Task<JsonDocument?> ReadNoteDocumentAsync(string collection, string noteKey, CancellationToken ct = default);
    Task<List<(string id, string targetCollection, string targetKey)>> ListPendingEmbeddingsAsync(int limit = 100, CancellationToken ct = default);

    // Delete
    Task<int> DeleteStalePostsAsync(IReadOnlyCollection<(string Collection, string Slug)> currentPosts, IReadOnlyCollection<string>? scopedCollections = null, CancellationToken ct = default);
    Task<int> DeleteStaleNotesAsync(IReadOnlyList<string> currentKeys, TenantId tenant, CancellationToken ct = default);

    // Query — thin query method for recall engine's raw queries (Query Object pattern, Fowler PoEAA)
    Task<List<T>> QueryAsync<T>(string query, Dictionary<string, object> bindVars, CancellationToken ct = default);
}
