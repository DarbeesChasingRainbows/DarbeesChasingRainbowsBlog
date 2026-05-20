namespace Darbee.Gateway.Domain.Models;

public enum MemoryKind
{
    Decision,
    Observation,
    Fact,
    Summary,
    Entity,
    Post,
}

public static class MemoryCollections
{
    public const string Decisions = "memory_decisions";
    public const string Observations = "memory_observations";
    public const string Facts = "memory_facts";
    public const string Summaries = "memory_summaries";
    public const string Entities = "memory_entities";
    public const string Edges = "memory_edges";
    public const string PendingEmbeddings = "memory_pending_embeddings";
    public const string Posts = "memory_posts";
    public const string Meta = "memory_meta";

    public static string ForKind(MemoryKind kind) => kind switch
    {
        MemoryKind.Decision => Decisions,
        MemoryKind.Observation => Observations,
        MemoryKind.Fact => Facts,
        MemoryKind.Summary => Summaries,
        MemoryKind.Entity => Entities,
        MemoryKind.Post => Posts,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
