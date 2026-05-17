using System.Diagnostics;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Endpoints;

public static class ContentRagEndpoints
{
    public static async Task<ReindexResponse> HandleReindexAsync(
        ReindexRequest request,
        MemoryStore store,
        IEmbeddingClient embeddings,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

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
        var deletedStale = await store.DeleteStalePostsAsync(currentSet, ct);

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
}
