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

    public MemoryPlugin(MemoryStore store, ITenantContextAccessor tenant)
    {
        _store = store;
        _tenant = tenant;
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
}
