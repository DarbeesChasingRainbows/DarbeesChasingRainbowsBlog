using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Ports;

namespace Darbee.Gateway.Tests.Contracts;

/// <summary>
/// LSP enforcement: every IMemoryRepository adapter must pass this contract.
/// Template Method pattern — the test algorithm is fixed; the subclass supplies
/// the adapter factory and a per-test setup/teardown via IAsyncLifetime.
/// </summary>
[Trait("Category", "Contract")]
public abstract class MemoryRepositoryContractTests : IAsyncLifetime
{
    protected IMemoryRepository Repository { get; private set; } = null!;
    protected IEmbeddingClient Embeddings { get; private set; } = null!;

    /// <summary>
    /// Subclass returns (repository, embeddings) for a freshly provisioned, isolated backend.
    /// Called once per test by xUnit's <see cref="IAsyncLifetime.InitializeAsync"/>.
    /// Return null to skip — the subclass should do this when its backend isn't enabled
    /// (e.g., when an environment flag is off).
    /// </summary>
    protected abstract Task<(IMemoryRepository repo, IEmbeddingClient emb)?> CreateRepositoryAsync();

    /// <summary>
    /// Subclass tears down whatever <see cref="CreateRepositoryAsync"/> provisioned.
    /// Called once per test by xUnit's <see cref="IAsyncLifetime.DisposeAsync"/>.
    /// </summary>
    protected abstract Task TeardownAsync();

    private bool _enabled;

    public async Task InitializeAsync()
    {
        var created = await CreateRepositoryAsync();
        if (created is null)
        {
            _enabled = false;
            return;
        }
        _enabled = true;
        Repository = created.Value.repo;
        Embeddings = created.Value.emb;
    }

    public async Task DisposeAsync()
    {
        if (!_enabled) return;
        Repository.Dispose();
        await TeardownAsync();
    }

    // ---- Test factories used by the contract suite ----

    private static PostDocument MakePost(string slug = "welcome", string collection = "blog") =>
        new PostDocument(
            Collection: collection,
            Slug: slug,
            Title: $"Post {slug}",
            Description: $"Description for {slug}.",
            Body: $"Body text for {slug}, contains keywords for retrieval.",
            AiSummary: $"Summary of {slug}",
            KeyTakeaways: new[] { "One" },
            Faq: Array.Empty<FaqEntry>(),
            EntityMentions: Array.Empty<string>(),
            Tags: new[] { "test" },
            Category: "Test",
            PubDate: "2026-05-19");

    private static NoteDocument MakeNote(
        string key = "obsidian://daily/note.md",
        MemoryKind kind = MemoryKind.Observation,
        string text = "I noticed the cast iron pan rusts in the trailer.",
        string tenant = "private") =>
        new NoteDocument(
            Key: key,
            Title: "Note",
            Text: text,
            Kind: kind,
            TenantId: tenant,
            Metadata: new Dictionary<string, object> { ["source"] = "obsidian" });

    // ---- Contract tests (LSP guarantees) ----

    [Fact]
    public async Task UpsertPostAsync_FreshPost_ReportsEmbeddedForBothVectors()
    {
        if (!_enabled) return;
        var result = await Repository.UpsertPostAsync(MakePost(slug: "fresh"), force: false);
        Assert.Equal("fresh", result.Slug);
        Assert.Equal(VectorWriteOutcome.Embedded, result.Summary);
        Assert.Equal(VectorWriteOutcome.Embedded, result.Body);
    }

    [Fact]
    public async Task UpsertPostAsync_SamePostTwice_SecondIsAllCacheHits()
    {
        if (!_enabled) return;
        await Repository.UpsertPostAsync(MakePost(slug: "cached"), force: false);
        var second = await Repository.UpsertPostAsync(MakePost(slug: "cached"), force: false);
        Assert.Equal(VectorWriteOutcome.Cached, second.Summary);
        Assert.Equal(VectorWriteOutcome.Cached, second.Body);
    }

    [Fact]
    public async Task UpsertPostAsync_ForceTrue_ReembedsEvenOnHashMatch()
    {
        if (!_enabled) return;
        await Repository.UpsertPostAsync(MakePost(slug: "forced"), force: false);
        var forced = await Repository.UpsertPostAsync(MakePost(slug: "forced"), force: true);
        Assert.Equal(VectorWriteOutcome.Embedded, forced.Summary);
        Assert.Equal(VectorWriteOutcome.Embedded, forced.Body);
    }

    [Fact]
    public async Task UpsertNoteAsync_KindRoutesToCorrectCollection()
    {
        if (!_enabled) return;
        var fact = await Repository.UpsertNoteAsync(
            MakeNote(key: "obsidian://f1.md", kind: MemoryKind.Fact));
        Assert.Equal(VectorWriteOutcome.Embedded, fact.Outcome);

        // Read via raw collection name — confirms the kind routing.
        using var inFacts = await Repository.ReadNoteDocumentAsync(MemoryCollections.Facts, "obsidian://f1.md");
        using var inObs = await Repository.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://f1.md");
        Assert.NotNull(inFacts);
        Assert.Null(inObs);
    }

    [Fact]
    public async Task UpsertNoteAsync_HashChanges_ReembedsAndOverwrites()
    {
        if (!_enabled) return;
        var first = await Repository.UpsertNoteAsync(
            MakeNote(key: "obsidian://hash.md", text: "first version"));
        var second = await Repository.UpsertNoteAsync(
            MakeNote(key: "obsidian://hash.md", text: "second version"));
        Assert.Equal(VectorWriteOutcome.Embedded, first.Outcome);
        Assert.Equal(VectorWriteOutcome.Embedded, second.Outcome);
    }

    [Fact]
    public async Task SearchAsync_DefaultKindsAndTenants_ReturnsPublicPostsOnly()
    {
        if (!_enabled) return;
        await Repository.UpsertPostAsync(MakePost(slug: "pubs"), force: false);
        await Repository.UpsertNoteAsync(MakeNote(key: "obsidian://priv.md"));

        var hits = await Repository.SearchAsync(
            new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            new[] { MemoryKind.Post },
            new[] { "public" },
            k: 5);

        Assert.NotEmpty(hits);
        // All results must be from the "public" tenant (posts always land there).
        Assert.All(hits, h => Assert.Equal("public", h.TenantId));
    }

    [Fact]
    public async Task SearchAsync_TenantIsolation_PrivateNeverLeaksWhenQueryingPublic()
    {
        if (!_enabled) return;
        // Identical-embedding stub means score collision; the only valid discriminator is tenant_id.
        await Repository.UpsertPostAsync(MakePost(slug: "publicPost"), force: false);
        await Repository.UpsertNoteAsync(MakeNote(key: "obsidian://privateNote.md"));

        // Query posts + observations across "public" tenant only.
        // The private note (tenant_id="private") must not appear.
        var hits = await Repository.SearchAsync(
            new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            new[] { MemoryKind.Post, MemoryKind.Observation },
            new[] { "public" },
            k: 10);

        // CRITICAL REGRESSION GUARD: no hit may carry a non-public tenant_id.
        Assert.All(hits, h =>
            Assert.False(
                h.TenantId != null && h.TenantId != "public",
                $"Tenant isolation breach: hit with tenant_id='{h.TenantId}' leaked into public query."));
    }

    [Fact]
    public async Task DeleteStalePostsAsync_RemovesPostsNotInCurrentSet()
    {
        if (!_enabled) return;
        await Repository.UpsertPostAsync(MakePost(slug: "one"), force: false);
        await Repository.UpsertPostAsync(MakePost(slug: "two"), force: false);

        var current = new[] { ("blog", "one") };
        var deleted = await Repository.DeleteStalePostsAsync(current, scopedCollections: null);

        Assert.True(deleted > 0, "expected at least one stale post (slug=two) to be deleted");
    }

    [Fact]
    public async Task DeleteStaleNotesAsync_ScopedByTenantAndSource()
    {
        if (!_enabled) return;
        await Repository.UpsertNoteAsync(MakeNote(key: "obsidian://a.md"));
        await Repository.UpsertNoteAsync(MakeNote(key: "obsidian://b.md"));

        var deleted = await Repository.DeleteStaleNotesAsync(Array.Empty<string>(), tenant: "private");

        Assert.Equal(2, deleted);
    }
}
