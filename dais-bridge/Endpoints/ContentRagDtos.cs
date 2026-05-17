namespace Darbee.Gateway.Endpoints;

public sealed record ReindexRequest(
    bool Force,
    IReadOnlyList<ReindexPost> Posts);

public sealed record ReindexPost(
    string Collection,
    string Slug,
    ReindexFrontmatter Frontmatter,
    string Body);

public sealed record ReindexFrontmatter(
    string Title,
    string Description,
    string? PubDate,
    string? Category,
    IReadOnlyList<string>? Tags,
    string? AiSummary,
    IReadOnlyList<string>? KeyTakeaways,
    IReadOnlyList<ReindexFaqEntry>? Faq,
    IReadOnlyList<string>? EntityMentions);

public sealed record ReindexFaqEntry(string Question, string Answer);

public sealed record ReindexResponse(
    int Scanned,
    int Embedded,
    int FromCache,
    int DeletedStale,
    long DurationMs,
    IReadOnlyList<ReindexPostResult> Posts);

public sealed record ReindexPostResult(
    string Slug,
    string Collection,
    string Summary,
    string Body,
    string? FailureReason = null);

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? Kinds,
    int K,
    string? Tenant);

public sealed record SearchResponse(
    long QueryEmbedMs,
    long SearchMs,
    IReadOnlyList<SearchResult> Results);

public sealed record SearchResult(
    string Slug,
    string Collection,
    string Title,
    string MatchedKind,
    double Score,
    string Snippet,
    string Url);

public sealed record MigrateRequest(string Confirm);
