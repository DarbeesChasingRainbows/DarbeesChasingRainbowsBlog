using System.Diagnostics;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Endpoints;

public static class ContentRagEndpoints
{
    public static async Task<ReindexResponse> HandleReindexAsync(
        ReindexRequest request,
        IMemoryRepository store,
        IEmbeddingClient embeddings,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (request.Posts.Count == 0)
            throw new ArgumentException("posts list must be non-empty (received 0 posts — refusing to run, this would wipe the index)");

        // Validate: reject duplicate (collection, slug) within request
        var seen = new HashSet<(string, string)>();
        foreach (var p in request.Posts)
        {
            if (!seen.Add((p.Collection, p.Slug)))
                throw new ArgumentException(
                    $"Duplicate (collection, slug): ({p.Collection}, {p.Slug})");
        }

        var results = new List<ReindexPostResult>();
        int embedded = 0, fromCache = 0;

        foreach (var p in request.Posts)
        {
            var post = ToPostDocument(p);
            try
            {
                var r = await store.UpsertPostAsync(post, request.Force, ct);
                results.Add(new ReindexPostResult(
                    Slug: r.Slug,
                    Collection: r.Collection,
                    Summary: OutcomeToString(r.Summary),
                    Body: OutcomeToString(r.Body)));
                embedded += (r.Summary == VectorWriteOutcome.Embedded ? 1 : 0)
                          + (r.Body == VectorWriteOutcome.Embedded ? 1 : 0);
                fromCache += (r.Summary == VectorWriteOutcome.Cached ? 1 : 0)
                           + (r.Body == VectorWriteOutcome.Cached ? 1 : 0);
            }
            catch (Exception ex)
            {
                results.Add(new ReindexPostResult(
                    Slug: p.Slug, Collection: p.Collection,
                    Summary: "failed", Body: "failed",
                    FailureReason: ex.Message));
            }
        }

        var currentSet = request.Posts
            .Select(p => (p.Collection, p.Slug))
            .ToList();
        var scopedCollections = request.Posts
            .Select(p => p.Collection)
            .Distinct()
            .ToList();
        var deletedStale = await store.DeleteStalePostsAsync(currentSet, scopedCollections, ct);

        sw.Stop();
        return new ReindexResponse(
            Scanned: request.Posts.Count,
            Embedded: embedded,
            FromCache: fromCache,
            DeletedStale: deletedStale,
            DurationMs: sw.ElapsedMilliseconds,
            Posts: results);
    }

    private static PostDocument ToPostDocument(ReindexPost p) =>
        new PostDocument(
            Collection: p.Collection,
            Slug: p.Slug,
            Title: p.Frontmatter.Title,
            Description: p.Frontmatter.Description,
            Body: p.Body,
            AiSummary: p.Frontmatter.AiSummary,
            KeyTakeaways: p.Frontmatter.KeyTakeaways ?? Array.Empty<string>(),
            Faq: (p.Frontmatter.Faq ?? Array.Empty<ReindexFaqEntry>())
                .Select(f => new FaqEntry(f.Question, f.Answer))
                .ToArray(),
            EntityMentions: p.Frontmatter.EntityMentions ?? Array.Empty<string>(),
            Tags: p.Frontmatter.Tags ?? Array.Empty<string>(),
            Category: p.Frontmatter.Category,
            PubDate: p.Frontmatter.PubDate);

    private static string OutcomeToString(VectorWriteOutcome o) => o switch
    {
        VectorWriteOutcome.Embedded => "embedded",
        VectorWriteOutcome.Cached => "cached",
        VectorWriteOutcome.Failed => "failed",
        _ => "unknown",
    };

    public static async Task<SearchResponse> HandleSearchAsync(
        SearchRequest request,
        IMemoryRepository store,
        IEmbeddingClient embeddings,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("query is required", nameof(request));

        var k = request.K <= 0 ? 5 : Math.Min(request.K, 50);

        // Tenants: prefer plural; fall back to singular for back-compat; default ["public"].
        IReadOnlyList<TenantId> tenants;
        if (request.Tenants is { Count: > 0 })
            tenants = request.Tenants.Select(t => new TenantId(t)).ToList();
        else if (!string.IsNullOrWhiteSpace(request.Tenant))
            tenants = new[] { new TenantId(request.Tenant!) };
        else
            tenants = new[] { new TenantId("public") };

        var kindStrings = request.Kinds is { Count: > 0 } ? request.Kinds : new[] { "post" };
        var kinds = kindStrings.Select(s => s.ToLowerInvariant() switch
        {
            "post" => MemoryKind.Post,
            "observation" => MemoryKind.Observation,
            "fact" => MemoryKind.Fact,
            "decision" => MemoryKind.Decision,
            _ => throw new ArgumentException($"unknown kind: {s}", nameof(request))
        }).ToList();

        var embedSw = Stopwatch.StartNew();
        var queryVec = await embeddings.EmbedAsync(request.Query, ct);
        embedSw.Stop();

        var searchSw = Stopwatch.StartNew();
        var rows = await store.SearchAsync(queryVec, kinds, tenants, k * 2, ct);
        searchSw.Stop();

        // Posts dedup application-side: best row per (collection, slug).
        var bestBySlug = new Dictionary<(string, string), PostSearchHit>();
        foreach (var row in rows)
        {
            var key = (row.Collection, row.Slug);
            if (!bestBySlug.TryGetValue(key, out var existing) || row.Sim > existing.Sim)
                bestBySlug[key] = row;
        }
        var topK = bestBySlug.Values.OrderByDescending(r => r.Sim).Take(k).ToList();

        var results = topK.Select(r =>
        {
            var kindLower = (r.Kind ?? "post").ToLowerInvariant();
            if (kindLower == "post")
            {
                return new SearchResult(
                    Slug: r.Slug,
                    Collection: r.Collection,
                    Title: r.Title,
                    MatchedKind: r.VectorKind,
                    Score: r.Sim,
                    Snippet: BuildSnippet(r),
                    Url: $"/{r.Collection}/{r.Slug}/",
                    Kind: "post",
                    Tenant: r.TenantId?.Value ?? "public");
            }
            // Notes: Slug = note_key, Collection = "", Url = note_key (obsidian://...).
            return new SearchResult(
                Slug: r.Slug,
                Collection: string.Empty,
                Title: r.Title,
                MatchedKind: kindLower,
                Score: r.Sim,
                Snippet: BuildSnippet(r),
                Url: r.Slug,
                Kind: kindLower,
                Tenant: r.TenantId?.Value ?? "private");
        }).ToList();

        return new SearchResponse(
            QueryEmbedMs: embedSw.ElapsedMilliseconds,
            SearchMs: searchSw.ElapsedMilliseconds,
            Results: results);
    }

    private const int SnippetMaxChars = 280;

    private static string BuildSnippet(PostSearchHit r)
    {
        var src = r.VectorKind == "summary"
            ? (r.AiSummary ?? r.Text ?? "")
            : (r.Text ?? "");
        if (src.Length <= SnippetMaxChars) return src;
        return src[..SnippetMaxChars] + "…";
    }

    public static async Task<IngestNotesResponse> HandleIngestNotesAsync(
        IngestNotesRequest request,
        IMemoryRepository store,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Tenant))
            throw new ArgumentException("tenant is required", nameof(request));

        var sw = Stopwatch.StartNew();

        int embedded = 0, cached = 0, failed = 0;
        var perNote = new List<IngestNoteResult>();

        foreach (var n in request.Notes)
        {
            MemoryKind kind;
            try
            {
                kind = n.Kind.ToLowerInvariant() switch
                {
                    "observation" => MemoryKind.Observation,
                    "fact" => MemoryKind.Fact,
                    "decision" => MemoryKind.Decision,
                    _ => throw new ArgumentException($"unsupported kind for note: {n.Kind}")
                };
            }
            catch (Exception ex)
            {
                failed++;
                perNote.Add(new IngestNoteResult(n.Key, "failed", ex.Message));
                continue;
            }

            var doc = new NoteDocument(
                Key: n.Key,
                Title: n.Title,
                Text: n.Text,
                Kind: kind,
                TenantId: new TenantId(request.Tenant),
                Metadata: n.Metadata);

            UpsertNoteResult r;
            try
            {
                r = await store.UpsertNoteAsync(doc, ct);
            }
            catch (Exception ex)
            {
                failed++;
                perNote.Add(new IngestNoteResult(n.Key, "failed", ex.Message));
                continue;
            }

            if (r.Outcome == VectorWriteOutcome.Embedded)
            {
                embedded++;
                perNote.Add(new IngestNoteResult(n.Key, "embedded", null));
            }
            else if (r.Outcome == VectorWriteOutcome.Cached)
            {
                cached++;
                perNote.Add(new IngestNoteResult(n.Key, "cached", null));
            }
            else
            {
                failed++;
                perNote.Add(new IngestNoteResult(n.Key, "failed", r.Reason));
            }
        }

        var staleDeleted = await store.DeleteStaleNotesAsync(request.CurrentKeys, new TenantId(request.Tenant), ct);

        sw.Stop();
        return new IngestNotesResponse(
            EmbeddedCount: embedded,
            CachedCount: cached,
            FailedCount: failed,
            StaleDeletedCount: staleDeleted,
            DurationMs: sw.ElapsedMilliseconds,
            PerNote: perNote);
    }

    public static async Task<MigrationResult> HandleMigrateAsync(
        MigrateRequest request,
        IEmbeddingMigrator migrator,
        CancellationToken ct = default)
    {
        return await migrator.MigrateEmbeddingsAsync(request.Confirm ?? "", ct);
    }
}
