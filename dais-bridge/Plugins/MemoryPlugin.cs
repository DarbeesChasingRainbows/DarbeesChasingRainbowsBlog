using System.ComponentModel;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Models;
using Microsoft.SemanticKernel;

namespace Darbee.Gateway.Plugins;

public sealed class MemoryPlugin
{
    private readonly MemoryStore _store;
    private readonly ITenantContextAccessor _tenant;
    private readonly MemoryRecallEngine _recall;

    public MemoryPlugin(MemoryStore store, ITenantContextAccessor tenant, MemoryRecallEngine recall)
    {
        _store = store;
        _tenant = tenant;
        _recall = recall;
    }

    [KernelFunction, Description("Records an architectural or design decision into long-term memory.")]
    public async Task<string> RememberDecision(
        [Description("What the decision is about")] string subject,
        [Description("What was chosen")] string chose,
        [Description("Why it was chosen")] string because,
        [Description("Alternatives considered (may be empty)")] IReadOnlyList<string> alternatives)
    {
        var t = _tenant.Required;
        var result = await _store.UpsertDecisionAsync(t.TenantId, subject, chose, because, alternatives);
        return JsonSerializer.Serialize(new { id = result.Id, completed = result.Completed, queued = result.Queued });
    }

    [KernelFunction, Description("Records a structured observation (commit, geo-run, research, asset action) into long-term memory.")]
    public async Task<string> RememberObservation(
        [Description("Source category: commit | geo-run | research | asset")] string source,
        [Description("Human-readable summary text")] string text,
        [Description("Optional JSON-serializable payload as a string")] string payloadJson)
    {
        var t = _tenant.Required;
        object payload = string.IsNullOrWhiteSpace(payloadJson) ? new { } : JsonSerializer.Deserialize<object>(payloadJson)!;
        var result = await _store.UpsertObservationAsync(t.TenantId, source, text, payload);
        return JsonSerializer.Serialize(new { id = result.Id, completed = result.Completed, queued = result.Queued });
    }

    [KernelFunction, Description("Connects two memory items with a typed edge.")]
    public async Task<string> LinkMemory(
        [Description("Full _id of source, e.g. memory_decisions/abc")] string fromId,
        [Description("Full _id of target")] string toId,
        [Description("Edge kind: mentions | depends-on | supersedes | tagged | about-file")] string edgeKind,
        [Description("Edge weight (default 1.0)")] double weight = 1.0)
    {
        var t = _tenant.Required;
        var result = await _store.UpsertEdgeAsync(t.TenantId, fromId, toId, edgeKind, weight);
        return result;
    }

    [KernelFunction, Description("Recalls memories most relevant to a query, combining graph expansion (via mentioned entities) and vector similarity. Tenant-scoped automatically.")]
    public async Task<string> Recall(
        [Description("Natural-language query")] string query,
        [Description("Maximum results (default 8)")] int topK = 8,
        [Description("Graph expansion hops from extracted entities (default 1)")] int expandHops = 1)
    {
        var t = _tenant.Required;
        var result = await _recall.RecallAsync(t.TenantId, query, topK, expandHops);
        return JsonSerializer.Serialize(new
        {
            extractedEntityIds = result.ExtractedEntityIds,
            items = result.Items.Select(i => new
            {
                kind = i.Item.Kind.ToString(),
                key = i.Item.Key,
                text = i.Item.Text,
                cosine = i.Cosine,
                proximity = i.Proximity,
                score = i.Score,
                hops = i.HopsFromQuery,
                path = i.PathEntityKeys,
            }),
        });
    }
}
