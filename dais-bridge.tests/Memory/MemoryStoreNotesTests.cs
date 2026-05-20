using System.Net.Http;
using System.Text.Json;
using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Domain.Ports;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.ValueObjects;
using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreNotesTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    internal sealed class StubEmbeddingClient : IEmbeddingClient
    {
        public int Dimension { get; set; } = 4;
        public int EmbedCalls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            EmbedCalls++;
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            EmbedCalls += texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToArray());
        }
    }

    internal static NoteDocument MakeNote(string key = "obsidian://daily/note.md",
                                           MemoryKind kind = MemoryKind.Observation,
                                           string text = "I noticed the cast iron pan rusts in the trailer.",
                                           TenantId? tenant = null) =>
        new NoteDocument(
            Key: key,
            Title: "Note",
            Text: text,
            Kind: kind,
            TenantId: tenant ?? new TenantId("private"),
            Metadata: new Dictionary<string, object> { ["source"] = "obsidian", ["tags"] = new[] { "rv" } });

    [Fact]
    public async Task UpsertNoteAsync_FreshNote_WritesOneDocToObservations()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            var result = await store.UpsertNoteAsync(MakeNote());

            Assert.Equal(VectorWriteOutcome.Embedded, result.Outcome);
            Assert.Equal(1, emb.EmbedCalls);

            using var doc = await store.ReadNoteDocumentAsync(MemoryCollections.Observations, MakeNote().Key);
            Assert.NotNull(doc);
            Assert.Equal("private", doc.RootElement.GetProperty("tenant_id").GetString());
            Assert.Equal("ready", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("obsidian", doc.RootElement.GetProperty("source").GetString());
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_SameNoteTwice_SecondIsCacheHit()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.UpsertNoteAsync(MakeNote());
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertNoteAsync(MakeNote());

            Assert.Equal(VectorWriteOutcome.Cached, result2.Outcome);
            Assert.Equal(callsAfterFirst, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_HashChanges_ReembedsAndOverwrites()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.UpsertNoteAsync(MakeNote(text: "first version"));
            var callsAfterFirst = emb.EmbedCalls;
            var result2 = await store.UpsertNoteAsync(MakeNote(text: "second version"));

            Assert.Equal(VectorWriteOutcome.Embedded, result2.Outcome);
            Assert.Equal(callsAfterFirst + 1, emb.EmbedCalls);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task UpsertNoteAsync_KindRoutesToCorrectCollection()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.UpsertNoteAsync(MakeNote(kind: MemoryKind.Fact, key: "obsidian://f1.md"));

            using var inFacts = await store.ReadNoteDocumentAsync(MemoryCollections.Facts, "obsidian://f1.md");
            using var inObs = await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://f1.md");

            Assert.NotNull(inFacts);
            Assert.Null(inObs);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task DeleteStaleNotesAsync_ScopedByTenantAndSource()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            // Seed 1 post + 2 obsidian notes + 1 private-tenant non-obsidian doc.
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "one"), force: false);
            await store.UpsertNoteAsync(MakeNote(key: "obsidian://a.md"));
            await store.UpsertNoteAsync(MakeNote(key: "obsidian://b.md"));
            await store.InsertRawPostAsync(new Dictionary<string, object?>
            {
                ["_key"] = "2797abc5b579165ba3714c3bec4dfee167c90b67", // Sha1Hex("manual://x.md")
                ["note_key"] = "manual://x.md",
                ["tenant_id"] = "private",
                ["kind"] = "observation",
                ["title"] = "Manual",
                ["text"] = "x",
                ["hash"] = "h",
                ["embedding"] = new float[] { 0.1f, 0.2f, 0.3f, 0.4f },
                ["status"] = "ready",
                ["source"] = (string?)null,
                ["metadata"] = new Dictionary<string, object>(),
                ["created_at"] = DateTime.UtcNow.ToString("o"),
                ["updated_at"] = DateTime.UtcNow.ToString("o"),
            }, MemoryCollections.Observations);

            // currentKeys empty -> all obsidian-sourced private notes should be removed.
            var deleted = await store.DeleteStaleNotesAsync(Array.Empty<string>(), new TenantId("private"));

            Assert.Equal(2, deleted);
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__one__summary"));
            Assert.NotNull(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "manual://x.md"));
            Assert.Null(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://a.md"));
            Assert.Null(await store.ReadNoteDocumentAsync(MemoryCollections.Observations, "obsidian://b.md"));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task DeleteStaleNotesAsync_DoesNotTouchMemoryPosts()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new ArangoMemoryRepository(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, new StubDomainEventDispatcher(), emb);

            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p1"), force: false);
            await store.UpsertPostAsync(MemoryStorePostsTests.MakePost(slug: "p2"), force: false);

            var deleted = await store.DeleteStaleNotesAsync(Array.Empty<string>(), new TenantId("private"));

            Assert.Equal(0, deleted);
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__p1__summary"));
            Assert.NotNull(await store.ReadPostDocumentAsync("blog__p2__summary"));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
