# Content RAG Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ingest `src/content/**/*.mdx` into Arango as embedded vectors via an extended `MemoryStore`, expose retrieval through a `POST /api/memory/search` HTTP endpoint, and resolve the stack drift from LM Studio to llama.cpp (qwen3-embedding-8b @ 4096-dim, chat at `:8080`, embed at `:8081`).

**Architecture:** Each post becomes two documents in a new `memory_posts` collection (one summary vector, one body vector), keyed by `{collection}__{slug}__{vector_kind}`. The bridge owns text composition, embedding, and storage; a thin `scripts/rag-reindex.mjs` orchestrates enumeration from the host. A new `memory_meta` collection holds an `embedding_config` sentinel that gates schema bootstrap on mismatch. `EnsureSchemaAsync` shifts from startup-eager to lazy first-use so a new `POST /api/admin/migrate-embeddings` endpoint can resolve mismatches without a chicken-and-egg startup deadlock.

**Tech Stack:** C# .NET 9, ASP.NET Core Minimal API, SignalR (existing, unchanged), ArangoDBNetStandard 3.x, xUnit. `llama.cpp/llama-server` on host for chat (`:8080`) and embeddings (`:8081`). Node 22.x for the `scripts/rag-reindex.mjs` runner.

**Spec:** [`docs/superpowers/specs/2026-05-16-content-rag-design.md`](../specs/2026-05-16-content-rag-design.md)

---

## File Structure

**New files:**
- `dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs` — renamed from `LmStudioEmbeddingClient.cs`
- `dais-bridge/Memory/Models/PostDocument.cs` — input record for `UpsertPostAsync`
- `dais-bridge/Memory/Models/UpsertPostResult.cs` — return record + `VectorWriteOutcome` enum
- `dais-bridge/Memory/Models/MigrationResult.cs` — return record + `EmbeddingConfig` record
- `dais-bridge/Memory/Models/FaqEntry.cs` — value record `(Question, Answer)`
- `dais-bridge/Memory/EmbeddingConfigMismatchException.cs` — custom exception
- `dais-bridge/Memory/PostTextComposer.cs` — pure static helpers for summary/body text composition
- `dais-bridge/Endpoints/ContentRagEndpoints.cs` — static handler functions registered by `Program.cs`
- `scripts/rag-reindex.mjs` — Node enumerator + bridge POSTer
- `scripts/lib/bridge-client.mjs` — small fetch wrapper, sharable with future `rag-search.mjs`
- `dais-bridge.tests/Memory/OpenAiCompatibleEmbeddingClientTests.cs` — renamed from `LmStudioEmbeddingClientTests.cs`
- `dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs` — sentinel + mismatch tests
- `dais-bridge.tests/Memory/MemoryStorePostsTests.cs` — UpsertPostAsync + DeleteStalePostsAsync + SearchAsync
- `dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs` — MigrateEmbeddingsAsync
- `dais-bridge.tests/Memory/PostTextComposerTests.cs` — pure unit tests of the composer
- `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs` — direct handler tests (no HTTP layer; uses real MemoryStore + stub embedder)
- `dais-bridge.tests/fixtures/content/` — small representative MDX corpus for integration tests

**Modified files:**
- `dais-bridge/Memory/Models/MemoryKind.cs` — add `Post` enum value + `Posts` and `Meta` collection constants
- `dais-bridge/Memory/MemoryStore.cs` — add `UpsertPostAsync`, `DeleteStalePostsAsync`, `SearchAsync`, `MigrateEmbeddingsAsync`; extend `EnsureSchemaAsync` for `memory_meta` + `memory_posts` + the `embedding_config` sentinel; convert to lazy-cached bootstrap
- `dais-bridge/Program.cs` — split `LLM_CHAT_URL` / `LLM_EMBEDDING_URL` env vars (with `LMSTUDIO_URL` back-compat), drop startup-eager `EnsureSchemaAsync`, register the three new endpoints
- `dais-bridge/appsettings.json` — change defaults to qwen3-embedding-8b / 4096-dim / split URLs
- `compose.yaml` — update env blocks for both dev + prod profiles, delete the `lm-probe` sidecar
- `.env` template / `make init` — rename `LMSTUDIO_URL` → `LLM_CHAT_URL`
- `package.json` — add `rag:reindex` (and optional `rag:search`) npm scripts
- `dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs` — extend to assert `memory_posts` and `memory_meta` are created
- `dais-bridge.tests/Memory/MemoryStoreVectorIndexTests.cs` — no functional change expected, but verify behavior holds at 4096-dim
- `HANDOFF.md` — add Phase 13 (Content RAG)
- `docs/superpowers/RESUME-graph-backed-rag.md` — add 2026-05-16 update note about stack drift + rename
- `docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md` — same update note
- `docs/superpowers/plans/2026-05-09-graph-backed-rag.md` — same update note
- `TODO-phase11.md` — same update note
- `CLAUDE.md` — add `rag:reindex` to the authoring-scripts command table

**Deleted files:**
- `dais-bridge/Memory/LmStudioEmbeddingClient.cs` — replaced by rename
- `dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs` — replaced by rename

---

## Conventions used throughout

- **xUnit test pattern:** `[Fact]` attribute, `Assert.*` helpers, class per concern. Async tests return `Task`.
- **Integration test gate:** Begin every Arango- or LLM-dependent test with an early `return` when its environment variable is absent. Pattern:
  ```csharp
  if (!ArangoEnabled) return;          // gates Arango
  if (!ArangoEnabled || !LlmEnabled) return;  // gates Arango+LLM combined
  ```
- **Per-test DB isolation:** Tests that touch Arango create a unique DB (`darbees_memory_test_<guid>`) and drop it in `finally`. See `MemoryStoreSchemaTests` for the existing helper pattern; new test classes follow the same convention.
- **Commit cadence:** One commit per task, immediately after the task's tests pass. Co-author trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

---

## Phase 0 — Branch + rename hygiene

The rename is mechanical and risk-free: pure name change with back-compat for the env var. Tests still pass without changes beyond the file/class name swap.

### Task 0.1: Create the feature branch

**Files:** none

- [ ] **Step 1: Confirm clean master**

Run: `git status --short`
Expected: working tree has the spec commit `b002248` and nothing else uncommitted that we care about. If there are unrelated WIP edits, stash them.

- [ ] **Step 2: Create and check out the branch**

Run:
```bash
git checkout -b feature/content-rag master
```
Expected: `Switched to a new branch 'feature/content-rag'`

### Task 0.2: Rename `LmStudioEmbeddingClient` → `OpenAiCompatibleEmbeddingClient`

**Files:**
- Rename: `dais-bridge/Memory/LmStudioEmbeddingClient.cs` → `dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs`
- Rename: `dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs` → `dais-bridge.tests/Memory/OpenAiCompatibleEmbeddingClientTests.cs`
- Modify: `dais-bridge/Program.cs` (DI registration)

- [ ] **Step 1: Move and rename the source file**

```bash
git mv dais-bridge/Memory/LmStudioEmbeddingClient.cs \
       dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs
```

- [ ] **Step 2: Rename the class inside**

In `dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs`, replace `LmStudioEmbeddingClient` with `OpenAiCompatibleEmbeddingClient` (one occurrence on line 7 of the class declaration; ctor name follows on line 16).

The full new file head:

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Darbee.Gateway.Memory;

public sealed class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _modelId;
    private readonly string? _apiKey;

    public int Dimension { get; }

    public OpenAiCompatibleEmbeddingClient(HttpClient http, string baseUrl, string modelId, int expectedDimension, string? apiKey = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _modelId = modelId;
        _apiKey = apiKey;
        Dimension = expectedDimension;
    }

    // ...rest of the class is unchanged from LmStudioEmbeddingClient.cs lines 25-69...
}
```

(Keep the rest of the file content identical — only the class name changes.)

- [ ] **Step 3: Update the DI registration in Program.cs**

In `dais-bridge/Program.cs`, find the line that currently reads:

```csharp
return new LmStudioEmbeddingClient(http, lmStudioUrl, embeddingModelId, embeddingDimension, lmStudioApiKey);
```

Replace with:

```csharp
return new OpenAiCompatibleEmbeddingClient(http, lmStudioUrl, embeddingModelId, embeddingDimension, lmStudioApiKey);
```

- [ ] **Step 4: Rename the test file and class**

```bash
git mv dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs \
       dais-bridge.tests/Memory/OpenAiCompatibleEmbeddingClientTests.cs
```

In the renamed test file, replace every occurrence of `LmStudioEmbeddingClient` (both the class name `LmStudioEmbeddingClientTests` and instantiations inside tests `new LmStudioEmbeddingClient(...)`) with `OpenAiCompatibleEmbeddingClient` / `OpenAiCompatibleEmbeddingClientTests`. The class declaration on line 9 and each `new LmStudioEmbeddingClient` constructor call need updating.

- [ ] **Step 5: Verify build + tests pass**

Run:
```bash
dotnet build dais-bridge.sln
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```
Expected: build succeeds with 0 warnings/0 errors. Test count unchanged from baseline (29 tests pass). All 3 `OpenAiCompatibleEmbeddingClientTests` tests pass under the new name.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Memory/OpenAiCompatibleEmbeddingClient.cs \
        dais-bridge/Memory/LmStudioEmbeddingClient.cs \
        dais-bridge.tests/Memory/OpenAiCompatibleEmbeddingClientTests.cs \
        dais-bridge.tests/Memory/LmStudioEmbeddingClientTests.cs \
        dais-bridge/Program.cs
git commit -m "$(cat <<'EOF'
refactor: rename LmStudioEmbeddingClient → OpenAiCompatibleEmbeddingClient

The class talks OpenAI-compatible /v1/embeddings, which works against
LM Studio, llama.cpp's llama-server, vLLM, Ollama, OpenAI itself, and
any other compliant server. The previous name was misleading. Behavior
is unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 1 — Stack-drift configuration

Switch from a single `LMSTUDIO_URL` (LM Studio assumption) to a split `LLM_CHAT_URL` / `LLM_EMBEDDING_URL`, update defaults to qwen3-embedding-8b + 4096-dim, update compose. Back-compat: if `LMSTUDIO_URL` is set and `LLM_CHAT_URL` is not, log a warning and use the old value.

### Task 1.1: Update `Program.cs` env-var reading

**Files:**
- Modify: `dais-bridge/Program.cs:23-48`

- [ ] **Step 1: Replace the existing env-var block**

In `dais-bridge/Program.cs`, find the block that currently reads (around lines 23-48):

```csharp
var lmStudioUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL")
    ?? builder.Configuration["AI:LMStudioUrl"]
    ?? "http://localhost:1234/v1";
var modelId = Environment.GetEnvironmentVariable("AI_MODEL_ID")
    ?? builder.Configuration["AI:ModelId"]
    ?? "local-model";
// ...
var embeddingModelId = builder.Configuration["AI:EmbeddingModelId"] ?? "nomic-embed-text-v1.5";
var embeddingDimension = int.Parse(builder.Configuration["AI:EmbeddingDimension"] ?? "768");
// ...
var lmStudioApiKey = Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY")
    ?? builder.Configuration["AI:LMStudioApiKey"];
```

Replace with:

```csharp
var legacyLmStudioUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL");
var lmChatUrl = Environment.GetEnvironmentVariable("LLM_CHAT_URL")
    ?? legacyLmStudioUrl
    ?? builder.Configuration["AI:ChatUrl"]
    ?? "http://localhost:8080/v1";
if (legacyLmStudioUrl is not null && Environment.GetEnvironmentVariable("LLM_CHAT_URL") is null)
{
    Console.WriteLine("[bridge] LMSTUDIO_URL is deprecated; rename to LLM_CHAT_URL in .env / compose.yaml.");
}

var lmEmbeddingUrl = Environment.GetEnvironmentVariable("LLM_EMBEDDING_URL")
    ?? builder.Configuration["AI:EmbeddingUrl"]
    ?? lmChatUrl;

var modelId = Environment.GetEnvironmentVariable("AI_MODEL_ID")
    ?? builder.Configuration["AI:ModelId"]
    ?? "llama-4-maverick";

var arangoUrl = Environment.GetEnvironmentVariable("ARANGO_URL")
    ?? builder.Configuration["ArangoDB:Url"]
    ?? "http://localhost:8529";
var arangoDb = Environment.GetEnvironmentVariable("ARANGO_DATABASE")
    ?? builder.Configuration["ArangoDB:Database"]
    ?? "darbees_knowledge";
var arangoUser = Environment.GetEnvironmentVariable("ARANGO_USER")
    ?? builder.Configuration["ArangoDB:User"]
    ?? "root";
var arangoPass = Environment.GetEnvironmentVariable("ARANGO_PASSWORD")
    ?? builder.Configuration["ArangoDB:Password"]
    ?? "password";

var embeddingModelId = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL_ID")
    ?? builder.Configuration["AI:EmbeddingModelId"]
    ?? "qwen3-embedding-8b";
var embeddingDimension = int.Parse(
    Environment.GetEnvironmentVariable("AI_EMBEDDING_DIMENSION")
    ?? builder.Configuration["AI:EmbeddingDimension"]
    ?? "4096");
var vectorNLists = int.Parse(builder.Configuration["Memory:VectorNLists"] ?? "100");

var lmApiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
    ?? Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY")
    ?? builder.Configuration["AI:ApiKey"];
```

- [ ] **Step 2: Update DI registration to use the new variables**

Still in `Program.cs`, find the `IEmbeddingClient` registration. Replace its body so the embedding client uses `lmEmbeddingUrl`, and the chat completion (kernel registrations near lines 84-111) uses `lmChatUrl`. Two replacements:

Before:
```csharp
return new OpenAiCompatibleEmbeddingClient(http, lmStudioUrl, embeddingModelId, embeddingDimension, lmStudioApiKey);
```
After:
```csharp
return new OpenAiCompatibleEmbeddingClient(http, lmEmbeddingUrl, embeddingModelId, embeddingDimension, lmApiKey);
```

Before (two places, one per kernel registration):
```csharp
kernelBuilder.AddOpenAIChatCompletion(modelId, lmStudioUrl);
```
After:
```csharp
kernelBuilder.AddOpenAIChatCompletion(modelId, lmChatUrl);
```

(The `lmStudioUrl` variable no longer exists; all references are now `lmChatUrl` for chat or `lmEmbeddingUrl` for embeddings.)

- [ ] **Step 3: Verify build still passes**

Run: `dotnet build dais-bridge.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add dais-bridge/Program.cs
git commit -m "$(cat <<'EOF'
feat(bridge): split LLM_CHAT_URL / LLM_EMBEDDING_URL env vars

Chat completion now reads LLM_CHAT_URL; embedding client reads
LLM_EMBEDDING_URL (falling back to LLM_CHAT_URL if only one endpoint
is configured). LMSTUDIO_URL still works as a back-compat fallback
for LLM_CHAT_URL with a deprecation warning. Embedding defaults
updated to qwen3-embedding-8b @ 4096-dim. AI_MODEL_ID default updated
to llama-4-maverick.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 1.2: Update `appsettings.json` defaults

**Files:**
- Modify: `dais-bridge/appsettings.json`

- [ ] **Step 1: Read current appsettings.json**

Run: `cat dais-bridge/appsettings.json`
Note the existing `AI` section.

- [ ] **Step 2: Update the AI section**

Replace the `AI` object with:

```json
{
  "AI": {
    "ChatUrl":          "http://localhost:8080/v1",
    "EmbeddingUrl":     "http://localhost:8081/v1",
    "ModelId":          "llama-4-maverick",
    "EmbeddingModelId": "qwen3-embedding-8b",
    "EmbeddingDimension": 4096,
    "ApiKey":           ""
  }
}
```

Delete any old `AI:LMStudioUrl`, `AI:LMStudioApiKey` keys.

- [ ] **Step 3: Verify the bridge can still start (smoke)**

Run:
```bash
dotnet build dais-bridge.sln
```
Expected: success. (We're not running the bridge yet — that requires llama.cpp + Arango + the next phase.)

- [ ] **Step 4: Commit**

```bash
git add dais-bridge/appsettings.json
git commit -m "$(cat <<'EOF'
feat(bridge): update appsettings.json defaults for llama.cpp + qwen3

Replaces LM Studio defaults with llama.cpp + qwen3-embedding-8b at
4096-dim. Old LMSTUDIO_* config keys removed; AI:ApiKey is the single
key sent as Bearer to both endpoints when set.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 1.3: Update `compose.yaml`

**Files:**
- Modify: `compose.yaml` (env blocks for `dais-bridge-dev` and `dais-bridge-prod`, delete `lm-probe` service)

- [ ] **Step 1: Delete the `lm-probe` sidecar**

In `compose.yaml`, delete the entire `lm-probe:` service block (currently approximately lines 20-39 — verify line range with `grep -n "lm-probe:" compose.yaml` first). The block runs an alpine container polling `:1234` and is no longer meaningful.

- [ ] **Step 2: Update `dais-bridge-dev` env**

In the `dais-bridge-dev:` service block, find the `environment:` section and update it to:

```yaml
    environment:
      ARANGO_URL: http://arango:8529
      ARANGO_USER: root
      ARANGO_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
      LLM_CHAT_URL: http://host.containers.internal:8080/v1
      LLM_EMBEDDING_URL: http://host.containers.internal:8081/v1
      AI_API_KEY: ${AI_API_KEY:-}
      AI_MODEL_ID: ${AI_MODEL_ID:-llama-4-maverick}
      AI_EMBEDDING_MODEL_ID: ${AI_EMBEDDING_MODEL_ID:-qwen3-embedding-8b}
      AI_EMBEDDING_DIMENSION: ${AI_EMBEDDING_DIMENSION:-4096}
      ARANGO_DATABASE: ${ARANGO_DATABASE:-darbees_knowledge}
      DOTNET_USE_POLLING_FILE_WATCHER: "true"
```

- [ ] **Step 3: Update `dais-bridge-prod` env**

Repeat for the `dais-bridge-prod:` service block — same `environment:` shape minus the `DOTNET_USE_POLLING_FILE_WATCHER` key.

- [ ] **Step 4: Verify compose parses**

Run:
```bash
podman compose -f compose.yaml config | grep -E "LLM_|AI_" | head -20
```
Expected: shows `LLM_CHAT_URL`, `LLM_EMBEDDING_URL`, `AI_MODEL_ID`, `AI_EMBEDDING_MODEL_ID`, `AI_EMBEDDING_DIMENSION` keys.

- [ ] **Step 5: Commit**

```bash
git add compose.yaml
git commit -m "$(cat <<'EOF'
feat(compose): replace LMSTUDIO_URL with LLM_CHAT_URL/LLM_EMBEDDING_URL

Drops the lm-probe sidecar (polled :1234 — LM Studio's default — which
no longer corresponds to anything running). Updates dev + prod env
blocks to use the split URL pattern, qwen3-embedding-8b at 4096-dim,
and llama-4-maverick as the default chat model alias.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 1.4: Update `.env` and the `make init` template

**Files:**
- Modify: `.env`
- Modify: `Makefile` (the `init:` target — find where the env template is generated)

- [ ] **Step 1: Update `.env` in place**

Edit `.env`:
- Rename `LMSTUDIO_URL=` line to `LLM_CHAT_URL=` (preserve value).
- Confirm `LLM_EMBEDDING_URL=` already exists (per prior session it does — value `http://0.0.0.0:8081/v1`).
- Confirm `AI_MODEL_ID=` is set to `llama-4-maverick` (update if it still says `llama-4-scout`).

Do NOT delete `LMSTUDIO_URL` if it exists — leave both keys until the back-compat code is removed in a later cleanup.

- [ ] **Step 2: Update the `make init` env template**

Find the `init:` target in `Makefile`:
```bash
grep -A 20 "^init:" Makefile
```
The target probably writes a default `.env` if missing, with `LMSTUDIO_URL=` in the template. Replace `LMSTUDIO_URL` with `LLM_CHAT_URL` in that template.

- [ ] **Step 3: Verify make init is still well-formed**

Run:
```bash
make help | head -10
```
Expected: `make` parses without error, `init` target listed.

- [ ] **Step 4: Commit**

```bash
git add .env Makefile
git commit -m "$(cat <<'EOF'
chore(env): rename LMSTUDIO_URL → LLM_CHAT_URL in .env and make init

.env is gitignored in production but tracked here as a dev template.
make init's default-write template uses the new name. Existing local
.env files with LMSTUDIO_URL continue to work via the back-compat
fallback in Program.cs.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2 — Add `MemoryKind.Post`, `memory_meta`, `memory_posts` collections

### Task 2.1: Extend `MemoryKind` enum and `MemoryCollections`

**Files:**
- Modify: `dais-bridge/Memory/Models/MemoryKind.cs`

- [ ] **Step 1: Write the failing test**

Create `dais-bridge.tests/Memory/MemoryKindTests.cs`:

```csharp
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

public class MemoryKindTests
{
    [Fact]
    public void ForKind_Post_ReturnsMemoryPostsCollectionName()
    {
        Assert.Equal("memory_posts", MemoryCollections.ForKind(MemoryKind.Post));
    }

    [Fact]
    public void Meta_CollectionConstant_IsMemoryMeta()
    {
        Assert.Equal("memory_meta", MemoryCollections.Meta);
    }

    [Fact]
    public void Posts_CollectionConstant_IsMemoryPosts()
    {
        Assert.Equal("memory_posts", MemoryCollections.Posts);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MemoryKindTests`
Expected: compile error — `Post` is not a member of `MemoryKind`, `Posts` and `Meta` are not members of `MemoryCollections`.

- [ ] **Step 3: Add the new members to `MemoryKind.cs`**

Open `dais-bridge/Memory/Models/MemoryKind.cs` and replace with:

```csharp
namespace Darbee.Gateway.Memory.Models;

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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MemoryKindTests`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/Models/MemoryKind.cs \
        dais-bridge.tests/Memory/MemoryKindTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): add MemoryKind.Post + memory_posts/memory_meta constants

Post joins the embeddable-content kinds. memory_posts holds two docs
per post (summary + body vector). memory_meta is the new singleton
config collection for the embedding_config sentinel that gates
schema-version safety.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 2.2: `EnsureSchemaAsync` creates `memory_meta` and `memory_posts`

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs:40-71` (the `EnsureSchemaAsync` method)
- Modify: `dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs` (extend existing test)

- [ ] **Step 1: Extend the existing schema test**

In `dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs`, find the existing `EnsureSchemaAsync_CreatesAllCollectionsAndPersistentIndexes_Idempotent` test. Inside its `try` block, after the existing `Assert.Contains` lines, add:

```csharp
Assert.Contains("memory_posts", collections);
Assert.Contains("memory_meta", collections);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~EnsureSchemaAsync_CreatesAllCollectionsAndPersistentIndexes_Idempotent`
Expected: test fails — `memory_posts` and `memory_meta` are not in the collections list.

- [ ] **Step 3: Extend `EnsureSchemaAsync` to create the new collections**

In `dais-bridge/Memory/MemoryStore.cs`, find the `EnsureSchemaAsync` method (around line 40). Update its collection list to include `MemoryCollections.Posts` and `MemoryCollections.Meta`:

```csharp
public async Task EnsureSchemaAsync(CancellationToken ct = default)
{
    foreach (var name in new[]
    {
        MemoryCollections.Decisions,
        MemoryCollections.Observations,
        MemoryCollections.Facts,
        MemoryCollections.Summaries,
        MemoryCollections.Entities,
        MemoryCollections.PendingEmbeddings,
        MemoryCollections.Posts,    // NEW
        MemoryCollections.Meta,     // NEW
    })
    {
        await EnsureCollectionAsync(name, isEdge: false);
    }

    await EnsureCollectionAsync(MemoryCollections.Edges, isEdge: true);

    foreach (var content in new[]
    {
        MemoryCollections.Decisions,
        MemoryCollections.Observations,
        MemoryCollections.Facts,
        MemoryCollections.Summaries
    })
    {
        await EnsurePersistentIndexAsync(content, new[] { "tenant_id", "status", "created_at" });
    }

    await EnsurePersistentIndexAsync(MemoryCollections.Entities, new[] { "tenant_id", "canonical_name" });
    await EnsurePersistentIndexAsync(MemoryCollections.Entities, new[] { "tenant_id", "aliases[*]" });
    await EnsurePersistentIndexAsync(MemoryCollections.Edges, new[] { "tenant_id", "kind" });

    // NEW: persistent indexes on memory_posts
    await EnsurePersistentIndexAsync(MemoryCollections.Posts, new[] { "tenant_id", "status", "vector_kind" });
    await EnsurePersistentIndexAsync(MemoryCollections.Posts, new[] { "collection", "slug" });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~EnsureSchemaAsync_CreatesAllCollectionsAndPersistentIndexes_Idempotent`
Expected: test passes.

- [ ] **Step 5: Run the full suite — nothing regressed**

Run: `ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj`
Expected: all tests pass (existing 29 + the 3 new MemoryKindTests + the extended schema assertions).

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge.tests/Memory/MemoryStoreSchemaTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): EnsureSchemaAsync creates memory_posts + memory_meta

Adds two persistent indexes on memory_posts:
  (tenant_id, status, vector_kind) — used by SearchAsync
  (collection, slug) — used by DeleteStalePostsAsync

memory_meta has no persistent index; it's a singleton config holder
addressed by deterministic _key (e.g., "embedding_config").

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3 — `embedding_config` sentinel and mismatch detection

This phase introduces the `EmbeddingConfigMismatchException`, the `memory_meta/embedding_config` sentinel, and the read-or-write-then-check logic at the heart of `EnsureSchemaAsync`. The lazy-bootstrap refactor (so the migration endpoint stays reachable) is in Phase 4.

### Task 3.1: Add `EmbeddingConfig` record and the exception type

**Files:**
- Create: `dais-bridge/Memory/EmbeddingConfigMismatchException.cs`
- Create: `dais-bridge/Memory/Models/EmbeddingConfig.cs`

- [ ] **Step 1: Write the failing test**

Create `dais-bridge.tests/Memory/EmbeddingConfigTests.cs`:

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

public class EmbeddingConfigTests
{
    [Fact]
    public void EmbeddingConfig_RecordEquality_MatchesByValue()
    {
        var a = new EmbeddingConfig("qwen3-embedding-8b", 4096);
        var b = new EmbeddingConfig("qwen3-embedding-8b", 4096);
        var c = new EmbeddingConfig("nomic-embed-text-v1.5", 768);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void EmbeddingConfigMismatchException_Message_IncludesBothConfigs()
    {
        var previous = new EmbeddingConfig("nomic-embed-text-v1.5", 768);
        var current = new EmbeddingConfig("qwen3-embedding-8b", 4096);

        var ex = new EmbeddingConfigMismatchException(previous, current);

        Assert.Contains("nomic-embed-text-v1.5", ex.Message);
        Assert.Contains("768", ex.Message);
        Assert.Contains("qwen3-embedding-8b", ex.Message);
        Assert.Contains("4096", ex.Message);
        Assert.Contains("/api/admin/migrate-embeddings", ex.Message);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EmbeddingConfigTests`
Expected: compile error — `EmbeddingConfig` and `EmbeddingConfigMismatchException` are not defined.

- [ ] **Step 3: Create the `EmbeddingConfig` record**

Create `dais-bridge/Memory/Models/EmbeddingConfig.cs`:

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record EmbeddingConfig(string Model, int Dimension);
```

- [ ] **Step 4: Create the exception**

Create `dais-bridge/Memory/EmbeddingConfigMismatchException.cs`:

```csharp
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

public sealed class EmbeddingConfigMismatchException : InvalidOperationException
{
    public EmbeddingConfig Previous { get; }
    public EmbeddingConfig Current { get; }

    public EmbeddingConfigMismatchException(EmbeddingConfig previous, EmbeddingConfig current)
        : base(BuildMessage(previous, current))
    {
        Previous = previous;
        Current = current;
    }

    private static string BuildMessage(EmbeddingConfig previous, EmbeddingConfig current) =>
        $"""
        Embedding config mismatch.
          In Arango: {{ model: {previous.Model}, dimension: {previous.Dimension} }}
          Bridge:    {{ model: {current.Model}, dimension: {current.Dimension} }}
        Existing vector indexes and embeddings are incompatible with the configured model.
        Remediation:
          curl -X POST http://localhost:5000/api/admin/migrate-embeddings \
               -H 'content-type: application/json' \
               -d '{{ "confirm": "preserve-and-reembed" }}'
        """;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~EmbeddingConfigTests`
Expected: both tests pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Memory/Models/EmbeddingConfig.cs \
        dais-bridge/Memory/EmbeddingConfigMismatchException.cs \
        dais-bridge.tests/Memory/EmbeddingConfigTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): add EmbeddingConfig record + mismatch exception

EmbeddingConfig is the (model, dimension) tuple persisted to
memory_meta/embedding_config. The mismatch exception's message
includes a runnable curl command for the remediation endpoint.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 3.2: `EnsureSchemaAsync` writes `embedding_config` on first run

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs` (extend `EnsureSchemaAsync`)
- Create: `dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs`

- [ ] **Step 1: Write the failing test**

Create `dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs`:

```csharp
using System.Net.Http;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreEmbeddingConfigTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    [Fact]
    public async Task EnsureSchemaAsync_FirstRun_WritesEmbeddingConfigSentinel()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                embeddingDimension: 4096, vectorNLists: 100, http);

            await store.EnsureSchemaAsync();

            var config = await store.ReadEmbeddingConfigAsync();
            Assert.NotNull(config);
            Assert.Equal(4096, config!.Dimension);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStoreEmbeddingConfigTests`
Expected: compile error — `MemoryStore.ReadEmbeddingConfigAsync` does not exist.

- [ ] **Step 3: Add `ReadEmbeddingConfigAsync` and the sentinel-write logic to `MemoryStore`**

In `dais-bridge/Memory/MemoryStore.cs`:

First, add a field for the bridge's configured embedding model id. The `MemoryStore` constructor currently doesn't take a model id — it only takes `embeddingDimension`. We need to pass the model id through. Update the constructor signature:

Find the constructor:
```csharp
public MemoryStore(string url, string db, string user, string pass, int embeddingDimension, int vectorNLists, HttpClient rawHttp, IEmbeddingClient? embeddings = null)
```

Replace with:
```csharp
public MemoryStore(string url, string db, string user, string pass, string embeddingModelId, int embeddingDimension, int vectorNLists, HttpClient rawHttp, IEmbeddingClient? embeddings = null)
{
    _baseUrl = url.TrimEnd('/');
    _db = db;
    _user = user;
    _pass = pass;
    _embeddingModelId = embeddingModelId;
    _embeddingDimension = embeddingDimension;
    _vectorNLists = vectorNLists;
    _rawHttp = rawHttp;
    _embeddings = embeddings;
    _transport = HttpApiTransport.UsingBasicAuth(new Uri(url), db, user, pass);
    _arango = new ArangoDBClient(_transport);
}
```

Add the corresponding field:
```csharp
private readonly string _embeddingModelId;
```

Add the read method and a write helper:
```csharp
public async Task<EmbeddingConfig?> ReadEmbeddingConfigAsync(CancellationToken ct = default)
{
    var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}/embedding_config";
    using var request = BuildAuthedRequest(HttpMethod.Get, url);
    using var response = await _rawHttp.SendAsync(request, ct);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync(ct);
    using var doc = JsonDocument.Parse(content);
    var model = doc.RootElement.GetProperty("model").GetString() ?? "";
    var dim = doc.RootElement.GetProperty("dimension").GetInt32();
    return new EmbeddingConfig(model, dim);
}

private async Task WriteEmbeddingConfigAsync(EmbeddingConfig config, bool isFirstTime, CancellationToken ct = default)
{
    var now = DateTime.UtcNow.ToString("O");
    var doc = new Dictionary<string, object?>
    {
        ["_key"] = "embedding_config",
        ["model"] = config.Model,
        ["dimension"] = config.Dimension,
        ["last_set_at"] = now,
    };
    if (isFirstTime) doc["first_set_at"] = now;

    var url = isFirstTime
        ? $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}"
        : $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Meta}/embedding_config";
    var method = isFirstTime ? HttpMethod.Post : HttpMethod.Patch;
    var (ok, errorNum, content) = await PostJsonRawAsync(url, doc, method);
    if (ok || errorNum == 1210 /* duplicate */) return;
    throw new InvalidOperationException($"Failed to write embedding_config (errorNum={errorNum}): {content}");
}
```

(The existing `PostJsonRawAsync` only supports POST. Extend it to take a method parameter, defaulting to POST so existing callers don't break:)

```csharp
private async Task<(bool ok, int errorNum, string content)> PostJsonRawAsync(string url, object body, HttpMethod? method = null)
{
    method ??= HttpMethod.Post;
    var request = BuildAuthedRequest(method, url);
    // ...rest unchanged...
}
```

At the end of `EnsureSchemaAsync`, after all the existing collection + index creation, add:

```csharp
    var current = new EmbeddingConfig(_embeddingModelId, _embeddingDimension);
    var stored = await ReadEmbeddingConfigAsync(ct);
    if (stored is null)
    {
        await WriteEmbeddingConfigAsync(current, isFirstTime: true, ct);
    }
    else if (!stored.Equals(current))
    {
        throw new EmbeddingConfigMismatchException(stored, current);
    }
```

- [ ] **Step 4: Update `Program.cs` to pass the model id to `MemoryStore`**

In `dais-bridge/Program.cs`, find the `MemoryStore` DI registration. Update the constructor call:

Before:
```csharp
return new MemoryStore(arangoUrl, arangoDb, arangoUser, arangoPass, embeddingDimension, vectorNLists, http, sp.GetRequiredService<IEmbeddingClient>());
```
After:
```csharp
return new MemoryStore(arangoUrl, arangoDb, arangoUser, arangoPass, embeddingModelId, embeddingDimension, vectorNLists, http, sp.GetRequiredService<IEmbeddingClient>());
```

- [ ] **Step 5: Update existing tests that construct `MemoryStore`**

Run: `grep -rn "new MemoryStore(" dais-bridge.tests/`

For each call site, add the model id as the 5th argument. Use a fixture value like `"test-embed-model"`:
```csharp
var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
    "test-embed-model", embeddingDimension: 768, vectorNLists: 1, http);
```

(The existing tests use various dimensions; preserve each test's specific values.)

- [ ] **Step 6: Run the new test plus the full suite**

Run: `ARANGO_TEST_RUN=1 dotnet test`
Expected: all tests pass (existing + new `EnsureSchemaAsync_FirstRun_WritesEmbeddingConfigSentinel`).

- [ ] **Step 7: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge/Program.cs \
        dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs \
        dais-bridge.tests/Memory/
git commit -m "$(cat <<'EOF'
feat(memory): EnsureSchemaAsync writes embedding_config on first run

MemoryStore now takes the embedding model id as a constructor
parameter. After collection bootstrap, EnsureSchemaAsync reads
memory_meta/embedding_config; absent → write the current
(model, dimension) tuple; present + matching → no-op; present +
mismatched → throw EmbeddingConfigMismatchException (handled in a
later task).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 3.3: `EnsureSchemaAsync` throws on mismatch

**Files:**
- Modify: `dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs` (add tests)

- [ ] **Step 1: Add two more tests**

In `MemoryStoreEmbeddingConfigTests.cs`, append:

```csharp
[Fact]
public async Task EnsureSchemaAsync_MismatchedConfig_ThrowsEmbeddingConfigMismatchException()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var first = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "nomic-embed-text-v1.5", embeddingDimension: 768, vectorNLists: 1, http);
        await first.EnsureSchemaAsync();

        var second = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 1, http);

        var ex = await Assert.ThrowsAsync<EmbeddingConfigMismatchException>(() => second.EnsureSchemaAsync());
        Assert.Equal("nomic-embed-text-v1.5", ex.Previous.Model);
        Assert.Equal(768, ex.Previous.Dimension);
        Assert.Equal("qwen3-embedding-8b", ex.Current.Model);
        Assert.Equal(4096, ex.Current.Dimension);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task EnsureSchemaAsync_MatchingConfig_DoesNotThrow_AndIsIdempotent()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

        await store.EnsureSchemaAsync();
        await store.EnsureSchemaAsync();  // second call — must not throw
        await store.EnsureSchemaAsync();  // third call — still no throw

        var config = await store.ReadEmbeddingConfigAsync();
        Assert.NotNull(config);
        Assert.Equal("qwen3-embedding-8b", config!.Model);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStoreEmbeddingConfigTests`
Expected: all 3 tests pass (the throw + the idempotent no-throw logic was already implemented in Task 3.2).

- [ ] **Step 3: Commit**

```bash
git add dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs
git commit -m "$(cat <<'EOF'
test(memory): EnsureSchemaAsync mismatch detection + idempotency

Two new tests cover the failure path (mismatched config → throw) and
the happy path (matching config → no-op even when run repeatedly).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 4 — Lazy bootstrap refactor

Move the schema bootstrap from Program.cs's startup block into a lazy-cached call inside MemoryStore. This is the design choice that lets the migrate-embeddings endpoint stay reachable when the bridge is in a mismatch state.

### Task 4.1: Add lazy-bootstrap behavior to `MemoryStore`

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs`

- [ ] **Step 1: Add the `_schemaReady` flag and `EnsureSchemaIfNeededAsync`**

In `MemoryStore.cs`, add a field:

```csharp
private volatile bool _schemaReady;
private readonly SemaphoreSlim _schemaLock = new(1, 1);
```

Add the lazy wrapper method right after `EnsureSchemaAsync`:

```csharp
public async Task EnsureSchemaIfNeededAsync(CancellationToken ct = default)
{
    if (_schemaReady) return;
    await _schemaLock.WaitAsync(ct);
    try
    {
        if (_schemaReady) return;
        await EnsureSchemaAsync(ct);
        _schemaReady = true;
    }
    finally
    {
        _schemaLock.Release();
    }
}

internal void InvalidateSchemaReady() => _schemaReady = false;
```

(`InvalidateSchemaReady` is internal — only `MigrateEmbeddingsAsync` calls it after a successful migration to force the next request to re-bootstrap.)

- [ ] **Step 2: Make every public store method call `EnsureSchemaIfNeededAsync` first**

The existing write methods (`UpsertDecisionAsync`, `UpsertObservationAsync`, `UpsertFactAsync`, `UpsertSummaryAsync`, `UpsertEntityAsync`, `UpsertEdgeAsync`, `ListPendingEmbeddingsAsync`) don't currently do this — they assume `EnsureSchemaAsync` ran at startup.

For each method, add at the very top (before any other work):

```csharp
await EnsureSchemaIfNeededAsync(ct);
```

This is 7 call sites. The dispose pattern stays unchanged.

- [ ] **Step 3: Remove the startup-eager bootstrap from `Program.cs`**

In `dais-bridge/Program.cs`, find the block:

```csharp
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<MemoryStore>();
    await store.EnsureSchemaAsync();
}
```

Delete it entirely. Schema is now lazy.

- [ ] **Step 4: Verify all existing tests still pass**

Run: `ARANGO_TEST_RUN=1 dotnet test`
Expected: all tests pass. The existing `MemoryStoreSchemaTests` still calls `EnsureSchemaAsync` explicitly, which is supported.

- [ ] **Step 5: Add a regression test that confirms lazy behavior**

In `MemoryStoreEmbeddingConfigTests.cs`, append:

```csharp
[Fact]
public async Task UpsertFactAsync_OnFreshStore_TriggersSchemaBootstrap()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

        // Do NOT call EnsureSchemaAsync explicitly — UpsertFactAsync should trigger it.
        await store.UpsertFactAsync("tenant-a", "the sky is blue", sourceThread: null);

        // Verify the schema was actually bootstrapped: read the config sentinel.
        var config = await store.ReadEmbeddingConfigAsync();
        Assert.NotNull(config);
        Assert.Equal("qwen3-embedding-8b", config!.Model);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 6: Run it**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~UpsertFactAsync_OnFreshStore_TriggersSchemaBootstrap`
Expected: test passes.

- [ ] **Step 7: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge/Program.cs \
        dais-bridge.tests/Memory/MemoryStoreEmbeddingConfigTests.cs
git commit -m "$(cat <<'EOF'
refactor(memory): EnsureSchemaAsync becomes lazy first-use

EnsureSchemaIfNeededAsync is now invoked by every public store method.
Schema bootstrap happens on first call rather than at app build time.
Program.cs's startup-eager EnsureSchemaAsync block is removed.

This is the design seam that lets MigrateEmbeddingsAsync (next task)
recover from a config mismatch without a deadlock at startup.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 5 — `PostDocument` record + `PostTextComposer`

Pure-logic helpers, fully unit-testable without Arango or LLM.

### Task 5.1: `PostDocument`, `FaqEntry`, and related records

**Files:**
- Create: `dais-bridge/Memory/Models/PostDocument.cs`
- Create: `dais-bridge/Memory/Models/FaqEntry.cs`
- Create: `dais-bridge/Memory/Models/UpsertPostResult.cs`

- [ ] **Step 1: Create `FaqEntry.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record FaqEntry(string Question, string Answer);
```

- [ ] **Step 2: Create `PostDocument.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record PostDocument(
    string Collection,
    string Slug,
    string Title,
    string Description,
    string Body,
    string? AiSummary,
    IReadOnlyList<string> KeyTakeaways,
    IReadOnlyList<FaqEntry> Faq,
    IReadOnlyList<string> EntityMentions,
    IReadOnlyList<string> Tags,
    string? Category,
    string? PubDate);
```

- [ ] **Step 3: Create `UpsertPostResult.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public enum VectorWriteOutcome
{
    Embedded,
    Cached,
    Failed,
}

public sealed record UpsertPostResult(
    string Slug,
    string Collection,
    VectorWriteOutcome Summary,
    VectorWriteOutcome Body,
    string? FailureReason = null);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/Models/FaqEntry.cs \
        dais-bridge/Memory/Models/PostDocument.cs \
        dais-bridge/Memory/Models/UpsertPostResult.cs
git commit -m "$(cat <<'EOF'
feat(memory): add PostDocument, FaqEntry, UpsertPostResult records

PostDocument is the input shape for UpsertPostAsync (next phase),
matching the shape the rag-reindex.mjs script will POST. FaqEntry
and VectorWriteOutcome are supporting types.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 5.2: `PostTextComposer` (summary + body)

**Files:**
- Create: `dais-bridge/Memory/PostTextComposer.cs`
- Create: `dais-bridge.tests/Memory/PostTextComposerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `dais-bridge.tests/Memory/PostTextComposerTests.cs`:

```csharp
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

public class PostTextComposerTests
{
    private static PostDocument SamplePost(
        string? aiSummary = "AI summary text",
        IReadOnlyList<string>? takeaways = null,
        IReadOnlyList<FaqEntry>? faq = null,
        IReadOnlyList<string>? mentions = null,
        string? category = "Faith",
        IReadOnlyList<string>? tags = null) =>
        new PostDocument(
            Collection: "blog",
            Slug: "welcome",
            Title: "Welcome",
            Description: "An intro post.",
            Body: "Hello from the road.",
            AiSummary: aiSummary,
            KeyTakeaways: takeaways ?? new[] { "One", "Two" },
            Faq: faq ?? new[] { new FaqEntry("Q1?", "A1.") },
            EntityMentions: mentions ?? new[] { "Kingdom Farm" },
            Tags: tags ?? new[] { "family" },
            Category: category,
            PubDate: "2026-04-29");

    [Fact]
    public void ComposeSummary_IncludesTitleDescriptionAiSummaryTakeawaysFaqMentions()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost());

        Assert.Contains("Welcome", text);
        Assert.Contains("An intro post.", text);
        Assert.Contains("AI Summary: AI summary text", text);
        Assert.Contains("- One", text);
        Assert.Contains("- Two", text);
        Assert.Contains("Q: Q1?", text);
        Assert.Contains("A: A1.", text);
        Assert.Contains("Mentions: Kingdom Farm", text);
    }

    [Fact]
    public void ComposeSummary_OmitsAiSummary_WhenNull()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(aiSummary: null));
        Assert.DoesNotContain("AI Summary:", text);
    }

    [Fact]
    public void ComposeSummary_OmitsFaqSection_WhenEmpty()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(faq: Array.Empty<FaqEntry>()));
        Assert.DoesNotContain("FAQ:", text);
        Assert.DoesNotContain("Q:", text);
    }

    [Fact]
    public void ComposeSummary_OmitsTakeawaysSection_WhenEmpty()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(takeaways: Array.Empty<string>()));
        Assert.DoesNotContain("Key Takeaways:", text);
    }

    [Fact]
    public void ComposeBody_IncludesTitleDescriptionTagsCategoryMentionsBody()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            tags: new[] { "family", "faith" },
            category: "Reflections",
            mentions: new[] { "Florida" }));

        Assert.Contains("Welcome", text);
        Assert.Contains("An intro post.", text);
        Assert.Contains("Tags: family, faith", text);
        Assert.Contains("Category: Reflections", text);
        Assert.Contains("Mentions: Florida", text);
        Assert.Contains("Hello from the road.", text);
    }

    [Fact]
    public void ComposeBody_OmitsCategoryAndTags_WhenAbsent()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            tags: Array.Empty<string>(),
            category: null));
        Assert.DoesNotContain("Tags:", text);
        Assert.DoesNotContain("Category:", text);
    }

    [Fact]
    public void ComposeBody_NeverEmitsLabelFollowedByNothing()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            mentions: Array.Empty<string>()));
        Assert.DoesNotMatch(@"Mentions:\s*\n", text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~PostTextComposerTests`
Expected: compile error — `PostTextComposer` is not defined.

- [ ] **Step 3: Create `PostTextComposer.cs`**

```csharp
using System.Text;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Memory;

public static class PostTextComposer
{
    public static string ComposeSummary(PostDocument post)
    {
        var sb = new StringBuilder();
        AppendIfNotEmpty(sb, post.Title);
        AppendIfNotEmpty(sb, post.Description);
        if (!string.IsNullOrWhiteSpace(post.AiSummary))
            AppendIfNotEmpty(sb, $"AI Summary: {post.AiSummary}");

        if (post.KeyTakeaways is { Count: > 0 })
        {
            sb.AppendLine("Key Takeaways:");
            foreach (var t in post.KeyTakeaways)
                sb.AppendLine($"- {t}");
            sb.AppendLine();
        }

        if (post.Faq is { Count: > 0 })
        {
            sb.AppendLine("FAQ:");
            foreach (var f in post.Faq)
            {
                sb.AppendLine($"Q: {f.Question}");
                sb.AppendLine($"A: {f.Answer}");
                sb.AppendLine();
            }
        }

        if (post.EntityMentions is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Mentions: {string.Join(", ", post.EntityMentions)}");

        return sb.ToString().TrimEnd() + "\n";
    }

    public static string ComposeBody(PostDocument post)
    {
        var sb = new StringBuilder();
        AppendIfNotEmpty(sb, post.Title);
        AppendIfNotEmpty(sb, post.Description);
        if (post.Tags is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Tags: {string.Join(", ", post.Tags)}");
        if (!string.IsNullOrWhiteSpace(post.Category))
            AppendIfNotEmpty(sb, $"Category: {post.Category}");
        if (post.EntityMentions is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Mentions: {string.Join(", ", post.EntityMentions)}");
        AppendIfNotEmpty(sb, post.Body);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendIfNotEmpty(StringBuilder sb, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        sb.AppendLine(line);
        sb.AppendLine();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~PostTextComposerTests`
Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/PostTextComposer.cs \
        dais-bridge.tests/Memory/PostTextComposerTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): PostTextComposer for summary + body embedding text

Pure static helpers that compose the two distinct strings embedded
per post. Defensively omits any field/section that's null or empty
— never emits a dangling label like "AI Summary:" with no body.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 6 — `UpsertPostAsync`

### Task 6.1: Write the first post (no caching yet)

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs`
- Create: `dais-bridge.tests/Memory/MemoryStorePostsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `dais-bridge.tests/Memory/MemoryStorePostsTests.cs`:

```csharp
using System.Net.Http;
using System.Text.Json;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStorePostsTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    private sealed class StubEmbeddingClient : IEmbeddingClient
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

    private static PostDocument SamplePost(string slug = "welcome", string collection = "blog") =>
        new PostDocument(
            Collection: collection,
            Slug: slug,
            Title: "Welcome",
            Description: "An intro post.",
            Body: "Hello from the road.",
            AiSummary: "Intro summary",
            KeyTakeaways: new[] { "One" },
            Faq: Array.Empty<FaqEntry>(),
            EntityMentions: Array.Empty<string>(),
            Tags: new[] { "family" },
            Category: "Faith",
            PubDate: "2026-04-29");

    [Fact]
    public async Task UpsertPostAsync_FreshPost_WritesTwoDocsSummaryAndBody()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var result = await store.UpsertPostAsync(SamplePost(), force: false);

            Assert.Equal("welcome", result.Slug);
            Assert.Equal("blog", result.Collection);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Summary);
            Assert.Equal(VectorWriteOutcome.Embedded, result.Body);
            Assert.Equal(2, emb.EmbedCalls);

            // Verify both docs exist with correct _key shape
            var summaryDoc = await store.ReadPostDocumentAsync("blog__welcome__summary");
            var bodyDoc = await store.ReadPostDocumentAsync("blog__welcome__body");
            Assert.NotNull(summaryDoc);
            Assert.NotNull(bodyDoc);
            Assert.Equal("summary", summaryDoc!.RootElement.GetProperty("vector_kind").GetString());
            Assert.Equal("body", bodyDoc!.RootElement.GetProperty("vector_kind").GetString());
            Assert.Equal("public", summaryDoc.RootElement.GetProperty("tenant_id").GetString());
            Assert.Equal("ready", summaryDoc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
```

- [ ] **Step 2: Run it to verify compile failure**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~UpsertPostAsync_FreshPost_WritesTwoDocsSummaryAndBody`
Expected: compile error — `UpsertPostAsync` and `ReadPostDocumentAsync` don't exist.

- [ ] **Step 3: Add `ReadPostDocumentAsync` to `MemoryStore`**

In `MemoryStore.cs`, add:

```csharp
public async Task<JsonDocument?> ReadPostDocumentAsync(string key, CancellationToken ct = default)
{
    await EnsureSchemaIfNeededAsync(ct);
    var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}/{key}";
    using var request = BuildAuthedRequest(HttpMethod.Get, url);
    using var response = await _rawHttp.SendAsync(request, ct);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync(ct);
    return JsonDocument.Parse(content);
}
```

- [ ] **Step 4: Add `UpsertPostAsync` to `MemoryStore`**

```csharp
public async Task<UpsertPostResult> UpsertPostAsync(
    PostDocument post,
    bool force,
    CancellationToken ct = default)
{
    await EnsureSchemaIfNeededAsync(ct);
    if (_embeddings is null)
        throw new InvalidOperationException("Embedding client is required for post upserts.");

    var summaryText = PostTextComposer.ComposeSummary(post);
    var bodyText = PostTextComposer.ComposeBody(post);

    var summaryOutcome = await UpsertOnePostVectorAsync(post, "summary", summaryText, force, ct);
    var bodyOutcome = await UpsertOnePostVectorAsync(post, "body", bodyText, force, ct);

    return new UpsertPostResult(post.Slug, post.Collection, summaryOutcome, bodyOutcome);
}

private async Task<VectorWriteOutcome> UpsertOnePostVectorAsync(
    PostDocument post, string vectorKind, string text, bool force, CancellationToken ct)
{
    var key = $"{post.Collection}__{post.Slug}__{vectorKind}";
    var hash = ComputeHash(text, _embeddingModelId);

    if (!force)
    {
        var existing = await ReadPostDocumentAsync(key, ct);
        if (existing is not null
            && existing.RootElement.TryGetProperty("hash", out var hashProp)
            && hashProp.GetString() == hash)
        {
            existing.Dispose();
            return VectorWriteOutcome.Cached;
        }
        existing?.Dispose();
    }

    var embedding = await _embeddings!.EmbedAsync(text, ct);
    var now = DateTime.UtcNow.ToString("O");
    var doc = new Dictionary<string, object?>
    {
        ["_key"] = key,
        ["slug"] = post.Slug,
        ["collection"] = post.Collection,
        ["vector_kind"] = vectorKind,
        ["tenant_id"] = "public",
        ["text"] = text,
        ["embedding"] = embedding,
        ["hash"] = hash,
        ["title"] = post.Title,
        ["description"] = post.Description,
        ["pub_date"] = post.PubDate,
        ["category"] = post.Category,
        ["tags"] = post.Tags,
        ["entity_mentions"] = post.EntityMentions,
        ["ai_summary"] = post.AiSummary,
        ["status"] = "ready",
        ["created_at"] = now,
        ["updated_at"] = now,
    };

    var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}?overwrite=true";
    var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
    if (!ok)
        throw new InvalidOperationException($"Post upsert failed (errorNum={errorNum}): {content}");

    return VectorWriteOutcome.Embedded;
}

private static string ComputeHash(string text, string modelId)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{modelId}:{text}"));
    return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~UpsertPostAsync_FreshPost_WritesTwoDocsSummaryAndBody`
Expected: pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge.tests/Memory/MemoryStorePostsTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): UpsertPostAsync writes summary + body docs to memory_posts

Each post produces two documents keyed by collection__slug__vector_kind
with tenant_id=public. Embedding hash incorporates the model id so
swapping models invalidates the cache.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 6.2: Hash-cache hit skips embedding

**Files:**
- Modify: `dais-bridge.tests/Memory/MemoryStorePostsTests.cs` (add tests)

- [ ] **Step 1: Add two tests**

Append to `MemoryStorePostsTests.cs`:

```csharp
[Fact]
public async Task UpsertPostAsync_SamePostTwice_SecondCallIsAllCacheHits()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.UpsertPostAsync(SamplePost(), force: false);
        var callsAfterFirst = emb.EmbedCalls;
        var result2 = await store.UpsertPostAsync(SamplePost(), force: false);

        Assert.Equal(VectorWriteOutcome.Cached, result2.Summary);
        Assert.Equal(VectorWriteOutcome.Cached, result2.Body);
        Assert.Equal(callsAfterFirst, emb.EmbedCalls);  // no additional embed calls
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task UpsertPostAsync_ForceTrue_ReembedsEvenOnHashMatch()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.UpsertPostAsync(SamplePost(), force: false);
        var callsAfterFirst = emb.EmbedCalls;
        var result2 = await store.UpsertPostAsync(SamplePost(), force: true);

        Assert.Equal(VectorWriteOutcome.Embedded, result2.Summary);
        Assert.Equal(VectorWriteOutcome.Embedded, result2.Body);
        Assert.Equal(callsAfterFirst + 2, emb.EmbedCalls);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStorePostsTests`
Expected: all pass (cache hit + force re-embed logic was implemented in Task 6.1).

- [ ] **Step 3: Commit**

```bash
git add dais-bridge.tests/Memory/MemoryStorePostsTests.cs
git commit -m "$(cat <<'EOF'
test(memory): UpsertPostAsync hash caching + force flag

Verifies cache-hit detection (no embed calls when hash matches) and
force=true semantics (re-embed even on hash match).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 7 — `DeleteStalePostsAsync`

### Task 7.1: Implement and test

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs`
- Modify: `dais-bridge.tests/Memory/MemoryStorePostsTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `MemoryStorePostsTests.cs`:

```csharp
[Fact]
public async Task DeleteStalePostsAsync_RemovesPostsNotInCurrentSet()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.UpsertPostAsync(SamplePost(slug: "one"), force: false);
        await store.UpsertPostAsync(SamplePost(slug: "two"), force: false);
        await store.UpsertPostAsync(SamplePost(slug: "three"), force: false);

        // current set keeps "one" and "three"; "two" should be deleted (both vectors)
        var current = new List<(string Collection, string Slug)>
        {
            ("blog", "one"),
            ("blog", "three"),
        };

        var deleted = await store.DeleteStalePostsAsync(current);

        Assert.Equal(2, deleted);  // summary + body for "two"
        Assert.Null(await store.ReadPostDocumentAsync("blog__two__summary"));
        Assert.Null(await store.ReadPostDocumentAsync("blog__two__body"));
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__one__summary"));
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__three__summary"));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Run it to verify failure**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~DeleteStalePostsAsync`
Expected: compile error — method doesn't exist.

- [ ] **Step 3: Implement `DeleteStalePostsAsync`**

In `MemoryStore.cs`:

```csharp
public async Task<int> DeleteStalePostsAsync(
    IReadOnlyCollection<(string Collection, string Slug)> currentPosts,
    CancellationToken ct = default)
{
    await EnsureSchemaIfNeededAsync(ct);

    var collectionSet = currentPosts.Select(p => p.Collection).Distinct().ToArray();
    var slugSet = currentPosts.Select(p => p.Slug).Distinct().ToArray();
    var pairs = currentPosts.Select(p => $"{p.Collection}__{p.Slug}").ToHashSet();

    var aql = """
        FOR doc IN @@col
          FILTER doc.tenant_id == "public"
          FILTER CONCAT(doc.collection, "__", doc.slug) NOT IN @pairs
          REMOVE doc IN @@col
          RETURN OLD._key
        """;
    var bindVars = new Dictionary<string, object>
    {
        ["@col"] = MemoryCollections.Posts,
        ["pairs"] = pairs.ToArray(),
    };
    var cursor = await _arango.Cursor.PostCursorAsync<string>(
        new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
        {
            Query = aql,
            BindVars = bindVars,
        });
    return cursor.Result.Count();
}
```

- [ ] **Step 4: Run test**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~DeleteStalePostsAsync`
Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge.tests/Memory/MemoryStorePostsTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): DeleteStalePostsAsync removes posts not in current set

AQL filters by tenant_id == public and removes docs whose
(collection, slug) pair isn't in the caller's set. Returns the count
of removed documents (typically 2× removed slugs since each slug has
summary + body vectors).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 8 — `SearchAsync` (AQL retrieval)

### Task 8.1: Empty collection returns empty result

**Files:**
- Modify: `dais-bridge/Memory/MemoryStore.cs`
- Modify: `dais-bridge.tests/Memory/MemoryStorePostsTests.cs`

- [ ] **Step 1: Failing test**

Append to `MemoryStorePostsTests.cs`:

```csharp
[Fact]
public async Task SearchAsync_EmptyCollection_ReturnsEmpty()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.EnsureSchemaAsync();

        var results = await store.SearchAsync(
            queryVec: new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            kinds: new[] { MemoryKind.Post },
            tenants: new[] { "public" },
            rawK: 10);

        Assert.Empty(results);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Verify it fails to compile**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~SearchAsync_EmptyCollection`
Expected: compile error.

- [ ] **Step 3: Implement `SearchAsync`**

In `MemoryStore.cs`:

```csharp
public async Task<List<ScoredMemoryItem>> SearchAsync(
    float[] queryVec,
    IReadOnlyList<MemoryKind> kinds,
    IReadOnlyList<string> tenants,
    int rawK,
    CancellationToken ct = default)
{
    await EnsureSchemaIfNeededAsync(ct);
    if (rawK < 1) throw new ArgumentOutOfRangeException(nameof(rawK), "must be >= 1");

    // Only `MemoryKind.Post` is supported in this phase; future Phase 11 work adds others.
    if (!kinds.Contains(MemoryKind.Post))
        return new List<ScoredMemoryItem>();

    var aql = """
        LET q = @query_vec
        FOR doc IN @@col
          FILTER doc.tenant_id IN @tenants
          FILTER doc.status == "ready"
          FILTER doc.vector_kind IN ["summary", "body"]
          LET sim = COSINE_SIMILARITY(doc.embedding, q)
          SORT sim DESC
          LIMIT @raw_k
          RETURN {
            key:         doc._key,
            slug:        doc.slug,
            collection:  doc.collection,
            vector_kind: doc.vector_kind,
            title:       doc.title,
            text:        doc.text,
            description: doc.description,
            ai_summary:  doc.ai_summary,
            pub_date:    doc.pub_date,
            category:    doc.category,
            tags:        doc.tags,
            sim:         sim
          }
        """;
    var bindVars = new Dictionary<string, object>
    {
        ["@col"] = MemoryCollections.Posts,
        ["query_vec"] = queryVec,
        ["tenants"] = tenants.ToArray(),
        ["raw_k"] = rawK,
    };

    var cursor = await _arango.Cursor.PostCursorAsync<SearchRow>(
        new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
        {
            Query = aql,
            BindVars = bindVars,
        });

    return cursor.Result.Select(r => new ScoredMemoryItem
    {
        Key = r.key,
        Slug = r.slug,
        Collection = r.collection,
        VectorKind = r.vector_kind,
        Title = r.title,
        Text = r.text,
        Description = r.description,
        AiSummary = r.ai_summary,
        PubDate = r.pub_date,
        Category = r.category,
        Tags = r.tags ?? Array.Empty<string>(),
        Sim = r.sim,
    }).ToList();
}

private sealed record SearchRow(
    string key, string slug, string collection, string vector_kind,
    string title, string text, string description, string? ai_summary,
    string? pub_date, string? category, IReadOnlyList<string>? tags, double sim);
```

Open `dais-bridge/Memory/Models/ScoredMemoryItem.cs` and **replace** with the richer post-aware shape:

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed class ScoredMemoryItem
{
    public string Key { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Collection { get; init; } = "";
    public string VectorKind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string Description { get; init; } = "";
    public string? AiSummary { get; init; }
    public string? PubDate { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public double Sim { get; init; }
}
```

(Verify by running `grep -rn "ScoredMemoryItem" dais-bridge/` — if any other code reads properties that no longer exist, fix the callers in the same commit. As of this plan, Phase 11 A1's record was minimal so this should be a clean replacement.)

- [ ] **Step 4: Run the empty-search test**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~SearchAsync_EmptyCollection`
Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge/Memory/Models/ScoredMemoryItem.cs \
        dais-bridge.tests/Memory/MemoryStorePostsTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): SearchAsync with brute-force COSINE_SIMILARITY AQL

ScoredMemoryItem reshaped to carry post-display metadata (title,
collection, slug, pub_date, etc.) so the search HTTP handler doesn't
need a second lookup. AQL filters by tenant + status + vector_kind
and sorts by descending cosine similarity.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 8.2: SearchAsync filters out pending_embedding status

**Files:**
- Modify: `dais-bridge.tests/Memory/MemoryStorePostsTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public async Task SearchAsync_FiltersOutPendingEmbeddingStatus()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        await store.UpsertPostAsync(SamplePost(slug: "ready"), force: false);

        // Manually insert a pending doc directly
        await store.InsertRawPostAsync(new Dictionary<string, object?>
        {
            ["_key"] = "blog__pending__summary",
            ["slug"] = "pending",
            ["collection"] = "blog",
            ["vector_kind"] = "summary",
            ["tenant_id"] = "public",
            ["status"] = "pending_embedding",
            ["embedding"] = new float[] { 0.1f, 0.2f, 0.3f, 0.4f },
            ["text"] = "x",
            ["title"] = "Pending Post",
            ["description"] = "",
        });

        var results = await store.SearchAsync(
            queryVec: new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            kinds: new[] { MemoryKind.Post },
            tenants: new[] { "public" },
            rawK: 10);

        Assert.All(results, r => Assert.NotEqual("pending", r.Slug));
        Assert.Contains(results, r => r.Slug == "ready");
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Add the `InsertRawPostAsync` test helper to `MemoryStore`**

```csharp
// Test-only helper. Bypasses embedding to insert a raw post doc.
internal async Task InsertRawPostAsync(Dictionary<string, object?> doc, CancellationToken ct = default)
{
    await EnsureSchemaIfNeededAsync(ct);
    var url = $"{_baseUrl}/_db/{_db}/_api/document/{MemoryCollections.Posts}?overwrite=true";
    var (ok, errorNum, content) = await PostJsonRawAsync(url, doc);
    if (!ok) throw new InvalidOperationException($"InsertRawPostAsync failed: {content}");
}
```

Make `MemoryStore`'s internal type visible to the test project. In `dais-bridge.csproj`, add (inside an `ItemGroup`):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="dais-bridge.tests" />
</ItemGroup>
```

- [ ] **Step 3: Run the test**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~SearchAsync_FiltersOutPendingEmbeddingStatus`
Expected: pass.

- [ ] **Step 4: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge/dais-bridge.csproj \
        dais-bridge.tests/Memory/MemoryStorePostsTests.cs
git commit -m "$(cat <<'EOF'
test(memory): SearchAsync excludes status=pending_embedding docs

Status filter in the AQL drops docs whose embeddings are stale
(post-migration before reindex). Test inserts a raw pending doc
via an internal test helper.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 9 — `MigrateEmbeddingsAsync`

### Task 9.1: Records and the basic preserve-and-reembed path

**Files:**
- Create: `dais-bridge/Memory/Models/MigrationResult.cs`
- Modify: `dais-bridge/Memory/MemoryStore.cs`
- Create: `dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs`

- [ ] **Step 1: Create `MigrationResult.cs`**

```csharp
namespace Darbee.Gateway.Memory.Models;

public sealed record MigrationResult(
    EmbeddingConfig? Previous,
    EmbeddingConfig Current,
    IReadOnlyList<string> IndexesDropped,
    IReadOnlyDictionary<string, int> DocsMarkedForReembed,
    int QueueSizeAfter);
```

- [ ] **Step 2: Write the failing test**

Create `dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs`:

```csharp
using System.Net.Http;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

[Trait("Category", "Integration")]
public class MemoryStoreMigrationTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static string ArangoUser => MemoryStoreSchemaTests.ArangoUser;
    private static string ArangoPass => MemoryStoreSchemaTests.ArangoPass;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    [Fact]
    public async Task MigrateEmbeddingsAsync_NoMismatch_IsNoop()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);

            await store.EnsureSchemaAsync();

            var result = await store.MigrateEmbeddingsAsync("preserve-and-reembed");

            Assert.NotNull(result.Previous);
            Assert.Equal(result.Previous, result.Current);
            Assert.Empty(result.IndexesDropped);
            Assert.Equal(0, result.QueueSizeAfter);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }

    [Fact]
    public async Task MigrateEmbeddingsAsync_RejectsInvalidConfirm()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var store = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
                "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);
            await store.EnsureSchemaAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => store.MigrateEmbeddingsAsync("nope"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.MigrateEmbeddingsAsync(""));
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
```

- [ ] **Step 3: Verify compile failure**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStoreMigrationTests`
Expected: compile error.

- [ ] **Step 4: Implement `MigrateEmbeddingsAsync` (preserve-and-reembed, no docs case)**

In `MemoryStore.cs`:

```csharp
public async Task<MigrationResult> MigrateEmbeddingsAsync(
    string confirmToken,
    CancellationToken ct = default)
{
    if (confirmToken != "preserve-and-reembed" && confirmToken != "wipe-and-reset")
        throw new ArgumentException(
            $"Invalid confirm token '{confirmToken}'. Accepted: 'preserve-and-reembed' or 'wipe-and-reset'.",
            nameof(confirmToken));

    // Ensure the meta collection exists (a minimal bootstrap — full schema not required to migrate).
    await EnsureCollectionAsync(MemoryCollections.Meta, isEdge: false);

    var current = new EmbeddingConfig(_embeddingModelId, _embeddingDimension);
    var previous = await ReadEmbeddingConfigAsync(ct);

    var indexesDropped = new List<string>();
    var docsMarked = new Dictionary<string, int>();
    var queueSizeBefore = 0;

    if (previous is not null && !previous.Equals(current))
    {
        // Real migration work: drop vector indexes + handle docs.
        foreach (var collection in new[]
        {
            MemoryCollections.Decisions,
            MemoryCollections.Observations,
            MemoryCollections.Facts,
            MemoryCollections.Summaries,
            MemoryCollections.Posts,
        })
        {
            // Drop vector indexes
            await EnsureCollectionAsync(collection, isEdge: false);
            var indexes = await ListIndexesAsync(collection);
            foreach (var idx in indexes.Where(i => i.Type == "vector"))
            {
                await DeleteIndexAsync(idx.Id);
                indexesDropped.Add($"{collection}/{idx.Id.Split('/').Last()}");
            }

            // Preserve canonical text, clear embedding fields
            int affected = 0;
            if (confirmToken == "preserve-and-reembed")
            {
                var aql = """
                    FOR doc IN @@col
                      FILTER doc.embedding != null
                      UPDATE doc WITH {
                        embedding: null,
                        status: "pending_embedding",
                        updated_at: DATE_ISO8601(DATE_NOW())
                      } IN @@col
                      RETURN OLD._key
                    """;
                var cursor = await _arango.Cursor.PostCursorAsync<string>(
                    new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                    {
                        Query = aql,
                        BindVars = new Dictionary<string, object> { ["@col"] = collection },
                    });
                var keys = cursor.Result.ToList();
                affected = keys.Count;

                foreach (var key in keys)
                    await EnqueuePendingEmbeddingAsync(collection, key);
            }
            else  // wipe-and-reset
            {
                var aql = """
                    FOR doc IN @@col
                      FILTER doc.embedding != null
                      REMOVE doc IN @@col
                      RETURN OLD._key
                    """;
                var cursor = await _arango.Cursor.PostCursorAsync<string>(
                    new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
                    {
                        Query = aql,
                        BindVars = new Dictionary<string, object> { ["@col"] = collection },
                    });
                affected = cursor.Result.Count();
            }

            docsMarked[collection] = affected;
        }

        // Clear in-memory vector-index-ready cache (next call will recheck)
        _vectorIndexReady.Clear();
    }

    // Always write/refresh the config doc with current values
    await WriteEmbeddingConfigAsync(current, isFirstTime: previous is null, ct);

    // Reset schema-ready so the next request re-runs EnsureSchemaAsync against fresh state
    _schemaReady = false;

    // Count queue size after
    var queueAql = "FOR p IN @@col COLLECT WITH COUNT INTO n RETURN n";
    var queueCursor = await _arango.Cursor.PostCursorAsync<int>(
        new ArangoDBNetStandard.CursorApi.Models.PostCursorBody
        {
            Query = queueAql,
            BindVars = new Dictionary<string, object> { ["@col"] = MemoryCollections.PendingEmbeddings },
        });
    var queueSizeAfter = queueCursor.Result.FirstOrDefault();

    return new MigrationResult(
        previous,
        current,
        indexesDropped,
        docsMarked,
        queueSizeAfter);
}
```

- [ ] **Step 5: Run the no-op + validation tests**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStoreMigrationTests`
Expected: both tests pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Memory/MemoryStore.cs \
        dais-bridge/Memory/Models/MigrationResult.cs \
        dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs
git commit -m "$(cat <<'EOF'
feat(memory): MigrateEmbeddingsAsync no-op + confirm-token validation

Handles the two trivial paths (no mismatch → idempotent no-op; invalid
confirm token → ArgumentException). The actual mismatch-recovery path
is exercised by the next task's tests.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 9.2: Migration preserves text, clears embeddings, enqueues

**Files:**
- Modify: `dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs`

- [ ] **Step 1: Add the mismatch-path test**

Append:

```csharp
[Fact]
public async Task MigrateEmbeddingsAsync_PreserveAndReembed_ClearsEmbedding_EnqueuesPending()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();

        // Seed at old config
        var oldStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "nomic-embed-text-v1.5", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await oldStore.UpsertPostAsync(MemoryStorePostsTests.MakePost("one"), force: false);

        // Pre-migration: doc is ready with embedding
        var pre = await oldStore.ReadPostDocumentAsync("blog__one__summary");
        Assert.NotNull(pre);
        Assert.Equal("ready", pre!.RootElement.GetProperty("status").GetString());

        // Now open a NEW store at NEW config and run migration
        var newStore = new MemoryStore(ArangoUrl, dbName, ArangoUser, ArangoPass,
            "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http, emb);

        var result = await newStore.MigrateEmbeddingsAsync("preserve-and-reembed");

        Assert.NotNull(result.Previous);
        Assert.Equal("nomic-embed-text-v1.5", result.Previous!.Model);
        Assert.Equal("qwen3-embedding-8b", result.Current.Model);
        Assert.True(result.DocsMarkedForReembed[MemoryCollections.Posts] >= 2,
            $"expected ≥2 posts marked, got {result.DocsMarkedForReembed[MemoryCollections.Posts]}");
        Assert.True(result.QueueSizeAfter >= 2);

        // Post-migration: doc has null embedding, pending status, text intact
        var post = await newStore.ReadPostDocumentAsync("blog__one__summary");
        Assert.NotNull(post);
        Assert.Equal("pending_embedding", post!.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, post.RootElement.GetProperty("embedding").ValueKind);
        Assert.Equal("Welcome", post.RootElement.GetProperty("title").GetString());
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

To support the cross-test fixture, expose `StubEmbeddingClient` and `MakePost` as internal in `MemoryStorePostsTests`:

In `MemoryStorePostsTests.cs`, change the access modifier on `StubEmbeddingClient` from `private sealed class` to `internal sealed class`, and rename `SamplePost` to a public-ish static helper `MakePost`:

```csharp
internal sealed class StubEmbeddingClient : IEmbeddingClient { /* unchanged */ }

internal static PostDocument MakePost(string slug = "welcome", string collection = "blog") =>
    new PostDocument(/* unchanged */);
```

(Within the same test class, references to `SamplePost(...)` are updated to `MakePost(...)`. There were 4 usages.)

- [ ] **Step 2: Run the migration tests**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~MemoryStoreMigrationTests`
Expected: all 3 migration tests pass.

- [ ] **Step 3: Commit**

```bash
git add dais-bridge.tests/Memory/MemoryStorePostsTests.cs \
        dais-bridge.tests/Memory/MemoryStoreMigrationTests.cs
git commit -m "$(cat <<'EOF'
test(memory): migration preserves text, clears embedding, enqueues

Exercises the full preserve-and-reembed path against a populated
collection. After migration: status=pending_embedding, embedding=null,
text intact, enqueued in memory_pending_embeddings.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 10 — HTTP endpoints

The endpoints live as static functions in `dais-bridge/Endpoints/ContentRagEndpoints.cs`. They take `MemoryStore` and `IEmbeddingClient` as parameters and return strongly-typed responses. Program.cs registers them via `app.MapPost(...)`. This shape is testable without a full `WebApplicationFactory` — we test the handlers directly.

### Task 10.1: Reindex endpoint — handler + Program registration

**Files:**
- Create: `dais-bridge/Endpoints/ContentRagEndpoints.cs`
- Modify: `dais-bridge/Program.cs`
- Create: `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`

- [ ] **Step 1: Define the request/response DTOs**

Create `dais-bridge/Endpoints/ContentRagDtos.cs`:

```csharp
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
```

- [ ] **Step 2: Write the failing handler test**

Create `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`:

```csharp
using System.Net.Http;
using Darbee.Gateway.Endpoints;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;
using Darbee.Gateway.Tests.Memory;

namespace Darbee.Gateway.Tests.Endpoints;

[Trait("Category", "Integration")]
public class ContentRagEndpointsTests
{
    private static string ArangoUrl => MemoryStoreSchemaTests.ArangoUrl;
    private static bool ArangoEnabled => MemoryStoreSchemaTests.ArangoEnabled;

    private static ReindexPost MakeReindexPost(string slug = "welcome") =>
        new ReindexPost(
            Collection: "blog",
            Slug: slug,
            Frontmatter: new ReindexFrontmatter(
                Title: "Welcome",
                Description: "intro",
                PubDate: "2026-04-29",
                Category: "Faith",
                Tags: new[] { "family" },
                AiSummary: "summary",
                KeyTakeaways: new[] { "one" },
                Faq: null,
                EntityMentions: null),
            Body: "Hello from the road.");

    [Fact]
    public async Task HandleReindexAsync_ColdStart_WritesTwoVectorsPerPost()
    {
        if (!ArangoEnabled) return;
        var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
        try
        {
            using var http = new HttpClient();
            var emb = new MemoryStorePostsTests.StubEmbeddingClient();
            var store = new MemoryStore(ArangoUrl, dbName,
                MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
                "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

            var request = new ReindexRequest(
                Force: false,
                Posts: new[] { MakeReindexPost("one"), MakeReindexPost("two") });

            var response = await ContentRagEndpoints.HandleReindexAsync(request, store, emb);

            Assert.Equal(2, response.Scanned);
            Assert.Equal(4, response.Embedded);  // 2 posts × 2 vectors
            Assert.Equal(0, response.FromCache);
            Assert.Equal(0, response.DeletedStale);
            Assert.Equal(2, response.Posts.Count);
        }
        finally
        {
            await MemoryStoreSchemaTests.DropDb(dbName);
        }
    }
}
```

- [ ] **Step 3: Verify compile failure**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~ContentRagEndpointsTests`
Expected: `ContentRagEndpoints` not found.

- [ ] **Step 4: Implement `ContentRagEndpoints.HandleReindexAsync`**

Create `dais-bridge/Endpoints/ContentRagEndpoints.cs`:

```csharp
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
```

- [ ] **Step 5: Register the endpoint in Program.cs**

In `Program.cs`, after the `app.MapHub(...)` lines, before `app.Run()`, add:

```csharp
app.MapPost("/api/admin/reindex-posts", async (
    ReindexRequest request,
    MemoryStore store,
    IEmbeddingClient embeddings,
    CancellationToken ct) =>
{
    try
    {
        var response = await ContentRagEndpoints.HandleReindexAsync(request, store, embeddings, ct);
        return Results.Ok(response);
    }
    catch (EmbeddingConfigMismatchException ex)
    {
        return Results.Json(new
        {
            error = "embedding_config_mismatch",
            message = ex.Message,
            previous = ex.Previous,
            current = ex.Current,
        }, statusCode: 503);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "invalid_request", details = ex.Message });
    }
});
```

(Add `using Darbee.Gateway.Endpoints;` at the top of Program.cs.)

- [ ] **Step 6: Run the test**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~HandleReindexAsync_ColdStart`
Expected: pass.

- [ ] **Step 7: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagEndpoints.cs \
        dais-bridge/Endpoints/ContentRagDtos.cs \
        dais-bridge/Program.cs \
        dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(bridge): POST /api/admin/reindex-posts endpoint

Static handler in Endpoints/ContentRagEndpoints. Validates payload,
calls UpsertPostAsync per post, then DeleteStalePostsAsync with the
full current set. Response shape includes per-post status and
aggregate counts.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 10.2: Reindex cache + stale-deletion coverage

**Files:**
- Modify: `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`

- [ ] **Step 1: Add tests**

Append:

```csharp
[Fact]
public async Task HandleReindexAsync_SameRequestTwice_SecondIsAllCacheHits()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        var request = new ReindexRequest(false, new[] { MakeReindexPost("one") });
        await ContentRagEndpoints.HandleReindexAsync(request, store, emb);
        var second = await ContentRagEndpoints.HandleReindexAsync(request, store, emb);

        Assert.Equal(0, second.Embedded);
        Assert.Equal(2, second.FromCache);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleReindexAsync_RemovedPost_DeletesStaleDocs()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        var first = new ReindexRequest(false, new[] {
            MakeReindexPost("keep-this"),
            MakeReindexPost("delete-this"),
        });
        await ContentRagEndpoints.HandleReindexAsync(first, store, emb);

        // Second call drops "delete-this"
        var second = new ReindexRequest(false, new[] { MakeReindexPost("keep-this") });
        var result = await ContentRagEndpoints.HandleReindexAsync(second, store, emb);

        Assert.Equal(2, result.DeletedStale);  // summary + body of delete-this
        Assert.Null(await store.ReadPostDocumentAsync("blog__delete-this__summary"));
        Assert.NotNull(await store.ReadPostDocumentAsync("blog__keep-this__summary"));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleReindexAsync_DuplicateSlugInPayload_Throws()
{
    var request = new ReindexRequest(false, new[] {
        MakeReindexPost("dup"),
        MakeReindexPost("dup"),
    });

    // No need for a real Arango call — validation runs before any store call.
    using var http = new HttpClient();
    var emb = new MemoryStorePostsTests.StubEmbeddingClient();
    var store = new MemoryStore("http://unused:8529", "unused", "u", "p",
        "m", 4, 1, http, emb);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        ContentRagEndpoints.HandleReindexAsync(request, store, emb));
}
```

- [ ] **Step 2: Run**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~ContentRagEndpointsTests`
Expected: all 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test(bridge): reindex cache hits, stale deletion, duplicate validation

Three more handler-level integration tests cover the idempotent
second call, stale-slug removal between calls, and the
duplicate-slug-in-payload validation path.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 10.3: Search endpoint

**Files:**
- Modify: `dais-bridge/Endpoints/ContentRagEndpoints.cs`
- Modify: `dais-bridge/Program.cs`
- Modify: `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `ContentRagEndpointsTests.cs`:

```csharp
[Fact]
public async Task HandleSearchAsync_ReturnsDedupedTopKByBestVectorKind()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);

        var seed = new ReindexRequest(false, new[] {
            MakeReindexPost("alpha"),
            MakeReindexPost("beta"),
            MakeReindexPost("gamma"),
        });
        await ContentRagEndpoints.HandleReindexAsync(seed, store, emb);

        var search = new SearchRequest(Query: "anything", Kinds: null, K: 2, Tenant: null);
        var result = await ContentRagEndpoints.HandleSearchAsync(search, store, emb);

        Assert.Equal(2, result.Results.Count);
        // Each slug appears at most once (deduped):
        Assert.Equal(result.Results.Select(r => r.Slug).Distinct().Count(), result.Results.Count);
        Assert.All(result.Results, r => Assert.Equal("blog", r.Collection));
        Assert.All(result.Results, r => Assert.StartsWith("/blog/", r.Url));
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleSearchAsync_EmptyCollection_ReturnsEmpty()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var emb = new MemoryStorePostsTests.StubEmbeddingClient();
        var store = new MemoryStore(ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "test-model", embeddingDimension: 4, vectorNLists: 1, http, emb);
        await store.EnsureSchemaAsync();

        var search = new SearchRequest(Query: "x", Kinds: null, K: 5, Tenant: null);
        var result = await ContentRagEndpoints.HandleSearchAsync(search, store, emb);

        Assert.Empty(result.Results);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}
```

- [ ] **Step 2: Verify failure**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~HandleSearchAsync_EmptyCollection`
Expected: compile error.

- [ ] **Step 3: Implement `HandleSearchAsync`**

In `ContentRagEndpoints.cs`, add:

```csharp
public static async Task<SearchResponse> HandleSearchAsync(
    SearchRequest request,
    MemoryStore store,
    IEmbeddingClient embeddings,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(request.Query))
        throw new ArgumentException("query is required", nameof(request));
    var k = request.K <= 0 ? 5 : Math.Min(request.K, 50);
    var tenant = string.IsNullOrWhiteSpace(request.Tenant) ? "public" : request.Tenant;

    var kindStrings = request.Kinds ?? new[] { "post" };
    var kinds = kindStrings.Select(s => s.ToLowerInvariant() switch
    {
        "post" => MemoryKind.Post,
        _ => throw new ArgumentException($"unknown kind: {s}", nameof(request))
    }).ToList();

    var embedSw = Stopwatch.StartNew();
    var queryVec = await embeddings.EmbedAsync(request.Query, ct);
    embedSw.Stop();

    var searchSw = Stopwatch.StartNew();
    var rows = await store.SearchAsync(queryVec, kinds, new[] { tenant }, rawK: k * 2, ct);
    searchSw.Stop();

    // Dedup application-side: best row per (collection, slug)
    var bestBySlug = new Dictionary<(string, string), ScoredMemoryItem>();
    foreach (var row in rows)
    {
        var key = (row.Collection, row.Slug);
        if (!bestBySlug.TryGetValue(key, out var existing) || row.Sim > existing.Sim)
            bestBySlug[key] = row;
    }
    var topK = bestBySlug.Values.OrderByDescending(r => r.Sim).Take(k).ToList();

    var results = topK.Select(r => new SearchResult(
        Slug: r.Slug,
        Collection: r.Collection,
        Title: r.Title,
        MatchedKind: r.VectorKind,
        Score: r.Sim,
        Snippet: BuildSnippet(r),
        Url: $"/{r.Collection}/{r.Slug}/")).ToList();

    return new SearchResponse(
        QueryEmbedMs: embedSw.ElapsedMilliseconds,
        SearchMs: searchSw.ElapsedMilliseconds,
        Results: results);
}

private const int SnippetMaxChars = 280;

private static string BuildSnippet(ScoredMemoryItem r)
{
    var src = r.VectorKind == "summary"
        ? (r.AiSummary ?? r.Text ?? "")
        : (r.Text ?? "");
    if (src.Length <= SnippetMaxChars) return src;
    return src[..SnippetMaxChars] + "…";
}
```

- [ ] **Step 4: Register in Program.cs**

```csharp
app.MapPost("/api/memory/search", async (
    SearchRequest request,
    MemoryStore store,
    IEmbeddingClient embeddings,
    CancellationToken ct) =>
{
    try
    {
        var response = await ContentRagEndpoints.HandleSearchAsync(request, store, embeddings, ct);
        return Results.Ok(response);
    }
    catch (EmbeddingConfigMismatchException ex)
    {
        return Results.Json(new { error = "embedding_config_mismatch", message = ex.Message },
            statusCode: 503);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "invalid_request", details = ex.Message });
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = "embedding_server_unreachable", message = ex.Message },
            statusCode: 503);
    }
});
```

- [ ] **Step 5: Run**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~HandleSearchAsync`
Expected: both tests pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagEndpoints.cs \
        dais-bridge/Program.cs \
        dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(bridge): POST /api/memory/search endpoint

Embeds query, runs SearchAsync with rawK = k*2, dedups by (collection,
slug) keeping the best vector_kind per post, returns top k with
snippets (aiSummary for summary hits, first 280 chars for body hits)
and URLs of the form /{collection}/{slug}/.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 10.4: Migrate-embeddings endpoint

**Files:**
- Modify: `dais-bridge/Endpoints/ContentRagEndpoints.cs`
- Modify: `dais-bridge/Program.cs`
- Modify: `dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs`

- [ ] **Step 1: Add the failing test**

Append:

```csharp
[Fact]
public async Task HandleMigrateAsync_NoMismatch_IsNoop()
{
    if (!ArangoEnabled) return;
    var dbName = await MemoryStoreSchemaTests.CreateUniqueDb();
    try
    {
        using var http = new HttpClient();
        var store = new MemoryStore(ArangoUrl, dbName,
            MemoryStoreSchemaTests.ArangoUser, MemoryStoreSchemaTests.ArangoPass,
            "qwen3-embedding-8b", embeddingDimension: 4096, vectorNLists: 100, http);
        await store.EnsureSchemaAsync();

        var request = new MigrateRequest("preserve-and-reembed");
        var result = await ContentRagEndpoints.HandleMigrateAsync(request, store);

        Assert.NotNull(result.Previous);
        Assert.Equal(result.Previous, result.Current);
    }
    finally
    {
        await MemoryStoreSchemaTests.DropDb(dbName);
    }
}

[Fact]
public async Task HandleMigrateAsync_InvalidConfirm_Throws()
{
    using var http = new HttpClient();
    var store = new MemoryStore("http://unused:8529", "unused", "u", "p",
        "m", 4, 1, http);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        ContentRagEndpoints.HandleMigrateAsync(new MigrateRequest("bad"), store));
}
```

- [ ] **Step 2: Implement `HandleMigrateAsync`**

```csharp
public static async Task<MigrationResult> HandleMigrateAsync(
    MigrateRequest request,
    MemoryStore store,
    CancellationToken ct = default)
{
    return await store.MigrateEmbeddingsAsync(request.Confirm ?? "", ct);
}
```

- [ ] **Step 3: Register in Program.cs**

```csharp
app.MapPost("/api/admin/migrate-embeddings", async (
    MigrateRequest request,
    MemoryStore store,
    CancellationToken ct) =>
{
    try
    {
        var result = await ContentRagEndpoints.HandleMigrateAsync(request, store, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new
        {
            error = "missing_or_invalid_confirm",
            message = ex.Message,
            accepted = new[] { "preserve-and-reembed", "wipe-and-reset" }
        });
    }
});
```

- [ ] **Step 4: Run**

Run: `ARANGO_TEST_RUN=1 dotnet test --filter FullyQualifiedName~HandleMigrateAsync`
Expected: both pass.

- [ ] **Step 5: Verify all bridge tests still green**

Run: `ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add dais-bridge/Endpoints/ContentRagEndpoints.cs \
        dais-bridge/Program.cs \
        dais-bridge.tests/Endpoints/ContentRagEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(bridge): POST /api/admin/migrate-embeddings endpoint

Thin wrapper over MemoryStore.MigrateEmbeddingsAsync. 400 with the
accepted-tokens hint on validation failure. The endpoint is exempt
from EnsureSchemaIfNeededAsync — MigrateEmbeddingsAsync does its own
minimal meta-collection bootstrap so this endpoint remains reachable
even when the bridge is in a config-mismatch state.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 11 — `scripts/rag-reindex.mjs`

### Task 11.1: Bridge HTTP client helper

**Files:**
- Create: `scripts/lib/bridge-client.mjs`

- [ ] **Step 1: Write the client**

```javascript
/**
 * Tiny HTTP wrapper for the dais-bridge gateway. Used by rag-reindex.mjs
 * and (optionally) rag-search.mjs. Exits 1 on transport errors so callers
 * don't need their own error plumbing for the common case.
 */

const DEFAULT_BRIDGE_URL = process.env.BRIDGE_URL || 'http://localhost:5000';

export class BridgeError extends Error {
	constructor(message, { status, body } = {}) {
		super(message);
		this.status = status;
		this.body = body;
	}
}

export async function bridgePost(path, body, { bridgeUrl = DEFAULT_BRIDGE_URL } = {}) {
	const url = `${bridgeUrl.replace(/\/$/, '')}${path}`;
	let response;
	try {
		response = await fetch(url, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify(body),
		});
	} catch (cause) {
		throw new BridgeError(`bridge unreachable at ${url}: ${cause.message}`);
	}
	const text = await response.text();
	let parsed;
	try {
		parsed = text ? JSON.parse(text) : null;
	} catch {
		parsed = text;
	}
	if (!response.ok) {
		throw new BridgeError(`bridge ${response.status}: ${text}`, {
			status: response.status,
			body: parsed,
		});
	}
	return parsed;
}
```

- [ ] **Step 2: Commit**

```bash
git add scripts/lib/bridge-client.mjs
git commit -m "$(cat <<'EOF'
feat(scripts): bridge HTTP client helper

Small fetch wrapper used by rag-reindex.mjs (and future rag-search).
Throws BridgeError with status + parsed body on non-2xx so callers
can inspect structured error responses from the bridge.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 11.2: `scripts/rag-reindex.mjs`

**Files:**
- Create: `scripts/rag-reindex.mjs`
- Modify: `package.json` (add `rag:reindex` script)

- [ ] **Step 1: Write the script**

Create `scripts/rag-reindex.mjs`:

```javascript
#!/usr/bin/env node
/**
 * Walk src/content/**\/*.mdx, POST structured posts to the dais-bridge
 * reindex endpoint, print a human-readable summary. Mirrors the pattern
 * of scripts/related-rebuild.mjs.
 */

import { listPosts, stripMdx, PRIMARY_COLLECTIONS, ALL_COLLECTIONS } from './lib/posts.mjs';
import { bridgePost, BridgeError } from './lib/bridge-client.mjs';

function parseArgs(argv) {
	const args = { force: false, collections: ALL_COLLECTIONS, bridgeUrl: undefined };
	for (let i = 2; i < argv.length; i++) {
		const a = argv[i];
		if (a === '--force') args.force = true;
		else if (a === '--collections') args.collections = argv[++i].split(',').map((s) => s.trim());
		else if (a === '--bridge-url') args.bridgeUrl = argv[++i];
		else if (a === '-h' || a === '--help') {
			console.log(
				'usage: rag-reindex [--force] [--collections blog,projects] [--bridge-url http://localhost:5000]'
			);
			process.exit(0);
		}
	}
	return args;
}

async function main() {
	const args = parseArgs(process.argv);

	const posts = await listPosts({ collections: args.collections });
	console.log(`Found ${posts.length} posts across ${args.collections.join(', ')}.`);

	const payload = {
		force: args.force,
		posts: posts.map((p) => ({
			collection: p.collection,
			slug: p.id,
			frontmatter: {
				title: p.frontmatter.title ?? '',
				description: p.frontmatter.description ?? '',
				pubDate: p.frontmatter.pubDate ?? null,
				category: p.frontmatter.category ?? null,
				tags: p.frontmatter.tags ?? [],
				aiSummary: p.frontmatter.aiSummary ?? null,
				keyTakeaways: p.frontmatter.keyTakeaways ?? [],
				faq: (p.frontmatter.faq ?? []).map((f) => ({ question: f.question, answer: f.answer })),
				entityMentions: p.frontmatter.entityMentions ?? [],
			},
			body: stripMdx(p.body),
		})),
	};

	try {
		const result = await bridgePost('/api/admin/reindex-posts', payload, {
			bridgeUrl: args.bridgeUrl,
		});

		for (const r of result.posts) {
			const symbol = r.summary === 'failed' || r.body === 'failed' ? '✗' : '✓';
			console.log(`${symbol} ${r.collection}/${r.slug}`);
			console.log(`  summary: ${r.summary}, body: ${r.body}`);
			if (r.failure_reason) console.log(`  ! ${r.failure_reason}`);
		}

		console.log('');
		console.log(
			`${result.scanned} posts: ${result.from_cache} cached, ${result.embedded} embedded, ${result.posts.filter((p) => p.summary === 'failed' || p.body === 'failed').length} failed`
		);
		console.log(`Deleted ${result.deleted_stale} stale doc(s).`);
		console.log(`Duration: ${(result.duration_ms / 1000).toFixed(1)}s`);

		const anyFailed = result.posts.some((p) => p.summary === 'failed' || p.body === 'failed');
		process.exit(anyFailed ? 1 : 0);
	} catch (err) {
		if (err instanceof BridgeError) {
			console.error(`bridge error (${err.status ?? 'no status'}): ${err.message}`);
			if (err.body) console.error(JSON.stringify(err.body, null, 2));
		} else {
			console.error(err.stack || err.message);
		}
		process.exit(1);
	}
}

main();
```

- [ ] **Step 2: Add the npm script**

In `package.json`, in the `"scripts"` object, add:

```json
"rag:reindex": "node --env-file-if-exists=.env scripts/rag-reindex.mjs"
```

(Place it alphabetically after `related:rebuild` to match the existing convention.)

- [ ] **Step 3: Verify the script parses**

Run:
```bash
node scripts/rag-reindex.mjs --help
```
Expected: usage line printed, exit code 0.

- [ ] **Step 4: Commit**

```bash
git add scripts/rag-reindex.mjs package.json
git commit -m "$(cat <<'EOF'
feat(scripts): rag-reindex.mjs walks content, POSTs to bridge

Reuses listPosts() and stripMdx() from scripts/lib/posts.mjs so the
"what is a publishable post" rules stay in one place. Prints per-post
status, aggregate counts, and exits 1 if any post failed.

Adds npm run rag:reindex.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 12 — End-to-end smoke

This is the first task that exercises the entire pipeline against real services. No new tests are written here — the goal is to confirm the parts compose correctly.

### Task 12.1: Smoke against live services

**Files:**
- None (verification only)

- [ ] **Step 1: Confirm prerequisite services**

Run:
```bash
curl -sf http://localhost:8080/v1/models | head -c 200 ; echo
curl -sf http://localhost:8081/v1/models | head -c 200 ; echo
curl -sf -u root:$(grep ^ARANGO_ROOT_PASSWORD .env | cut -d= -f2) http://localhost:8529/_api/version | head -c 200 ; echo
```
Expected: each returns a JSON body. If any returns nothing or fails, start the corresponding service before continuing.

- [ ] **Step 2: Bring up the bridge stack**

```bash
make up
make health
```
Expected: bridge container is up, health command returns successfully.

- [ ] **Step 3: Run the reindex**

```bash
npm run rag:reindex
```
Expected: output lists ~13 posts, all `embedded`, 0 stale deleted, duration a few seconds.

- [ ] **Step 4: Verify Arango has the docs**

```bash
podman exec -i $(podman ps -q --filter name=arango) \
  arangosh --server.endpoint http+tcp://localhost:8529 \
           --server.username root \
           --server.password "$(grep ^ARANGO_ROOT_PASSWORD .env | cut -d= -f2)" \
           --server.database darbees_knowledge \
           --javascript.execute-string \
  'print(db.memory_posts.count()); print(db.memory_meta.embedding_config);'
```
Expected: count is ~26 (13 posts × 2 vectors). `embedding_config` shows `qwen3-embedding-8b` / `4096`.

- [ ] **Step 5: Test retrieval via curl**

```bash
curl -s -X POST http://localhost:5000/api/memory/search \
     -H 'content-type: application/json' \
     -d '{"query":"cast iron pan","k":3}' | python3 -m json.tool
```
Expected: JSON response with `results[]` of length 3, each with `slug`, `score`, `snippet`, `url`. The `what-we-pack-first-in-the-rv` post should be in the top result for "cast iron pan."

- [ ] **Step 6: Test cache hits — second reindex**

```bash
npm run rag:reindex
```
Expected: all 26 vectors `cached`, 0 embedded, 0 stale deleted, duration well under 1s.

- [ ] **Step 7: Commit a smoke-test marker (optional)**

If everything passes, no code change is needed. The next phase updates docs. If you want a record of the smoke pass:

```bash
git commit --allow-empty -m "test: phase 12 smoke pass — bridge + reindex + search live"
```

---

## Phase 13 — Docs

### Task 13.1: Update Phase 11 doc artifacts with rename + stack-drift notes

**Files:**
- Modify: `docs/superpowers/RESUME-graph-backed-rag.md`
- Modify: `docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md`
- Modify: `docs/superpowers/plans/2026-05-09-graph-backed-rag.md`
- Modify: `TODO-phase11.md`

- [ ] **Step 1: Add a 2026-05-16 update note at the top of each file**

Add this block right after the H1 of each of the four files (preserve existing content below):

```markdown
> **2026-05-16 — Stack drift update:** This work was paused at task A6. Since then:
> - `LmStudioEmbeddingClient` has been renamed to `OpenAiCompatibleEmbeddingClient` (same behavior, accurate name).
> - The embedding stack switched from LM Studio + `nomic-embed-text-v1.5` (768-dim) to llama.cpp + `qwen3-embedding-8b` (4096-dim).
> - The bridge now reads `LLM_CHAT_URL` and `LLM_EMBEDDING_URL` (split). `LMSTUDIO_URL` is back-compat with a deprecation warning.
> - `EnsureSchemaAsync` is now invoked lazily (first-use) rather than at startup, so the new `POST /api/admin/migrate-embeddings` endpoint stays reachable during config-mismatch states.
> - Posts are now stored as `MemoryKind.Post` in a `memory_posts` collection. See [`docs/superpowers/specs/2026-05-16-content-rag-design.md`](specs/2026-05-16-content-rag-design.md).
>
> When resuming Phase 11 task B2 and beyond, the above is the current state. `MemoryStore` and `IEmbeddingClient` references in the original spec/plan below are correct in spirit; just substitute the new class name and config keys.
```

- [ ] **Step 2: Update collection-name references in the older spec**

In `docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md`, do a targeted find-and-replace where the older spec mentions collection names — make sure they match the `memory_*` prefix the code actually uses. Run:

```bash
grep -nE "(decisions|observations|facts|summaries|entities|edges|pending_embeddings)" docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md | grep -v memory_ | head -20
```

For each match, if it's a reference to the collection name, update to `memory_*`. Skip plain English usage like "decisions and observations are stored in...".

- [ ] **Step 3: Verify**

```bash
grep -nE "LmStudioEmbeddingClient|LMSTUDIO_URL" docs/superpowers/{RESUME,specs,plans}/*.md
```
Expected: each match is in a "2026-05-16 update note" or in clearly historical context. No silent references to the old names that would mislead a reader.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/RESUME-graph-backed-rag.md \
        docs/superpowers/specs/2026-05-09-graph-backed-rag-design.md \
        docs/superpowers/plans/2026-05-09-graph-backed-rag.md \
        TODO-phase11.md
git commit -m "$(cat <<'EOF'
docs(phase11): 2026-05-16 stack-drift + rename update notes

Adds an inline 'state of the world' note at the top of each Phase 11
artifact so a future resumer knows the embedding stack changed
(LM Studio → llama.cpp, nomic 768 → qwen3 4096), the client was
renamed, and EnsureSchemaAsync is now lazy. Collection-name
references inside the older spec are corrected to match the
memory_* prefix the code uses.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

### Task 13.2: HANDOFF + CLAUDE.md + a new RESUME guide

**Files:**
- Modify: `HANDOFF.md`
- Modify: `CLAUDE.md`
- Create: `docs/superpowers/RESUME-content-rag.md`

- [ ] **Step 1: Add a Phase 13 section to HANDOFF.md**

After the existing Phase 12 section in `HANDOFF.md`, insert:

```markdown
### Phase 13 — Content RAG (2026-05-16, complete on `feature/content-rag`)

Branch: `feature/content-rag` (off `master`). Spec: [`docs/superpowers/specs/2026-05-16-content-rag-design.md`](docs/superpowers/specs/2026-05-16-content-rag-design.md). Plan: [`docs/superpowers/plans/2026-05-16-content-rag.md`](docs/superpowers/plans/2026-05-16-content-rag.md). Resume guide: [`docs/superpowers/RESUME-content-rag.md`](docs/superpowers/RESUME-content-rag.md).

Adds `MemoryKind.Post` and a `memory_posts` collection alongside the existing chat-memory collections. Each post becomes two embedded vectors (summary + body), keyed by `{collection}__{slug}__{vector_kind}`. New HTTP endpoints on the bridge: `POST /api/admin/reindex-posts` (called by `npm run rag:reindex`), `POST /api/memory/search`, `POST /api/admin/migrate-embeddings`. A new `memory_meta/embedding_config` sentinel doc gates schema-version safety; mismatches throw `EmbeddingConfigMismatchException` and the migrate endpoint is the documented remediation path.

**Stack drift resolved in the same branch:** LM Studio → llama.cpp on host (`:8080` chat, `:8081` embed); `nomic-embed-text-v1.5` (768-dim) → `qwen3-embedding-8b` (4096-dim); `LMSTUDIO_URL` → split `LLM_CHAT_URL` / `LLM_EMBEDDING_URL` (back-compat warning). `LmStudioEmbeddingClient` renamed to `OpenAiCompatibleEmbeddingClient`. `EnsureSchemaAsync` shifted from startup-eager to lazy first-use.

**Working state:** `ARANGO_TEST_RUN=1 dotnet test` passes; `npm run rag:reindex` populates Arango from `src/content/**/*.mdx`; `curl -X POST :5000/api/memory/search` returns ranked posts.
```

- [ ] **Step 2: Add `rag:reindex` to CLAUDE.md's authoring-scripts command table**

In `CLAUDE.md`, find the "Commands (Authoring scripts — Phase 13)" section (note: this may currently say "Phase 13" or whatever the related-rebuild section was named). Add a row:

```markdown
| Reindex content RAG       | `npm run rag:reindex`           | After adding/editing posts; requires the bridge stack to be up (`make up`) and llama.cpp servers on :8080 + :8081 |
```

- [ ] **Step 3: Create `docs/superpowers/RESUME-content-rag.md`**

```markdown
# Resume Guide — Content RAG

> **Purpose:** Everything you (or a future agent session) need to pick this work back up cold without re-deriving context.

**Last updated:** 2026-05-16
**Branch:** `feature/content-rag` (off `master`)
**Status:** Spec + plan committed; implementation underway. See `TODO-content-rag.md` for the live punchlist if one exists; otherwise the plan itself is the source of truth.

---

## Quick start

```bash
# 1. Repo
git checkout feature/content-rag
git pull --ff-only

# 2. Local services (host-side llama.cpp + Podman Arango)
# 2a. Make sure llama.cpp llama-server is running:
#       chat: :8080 (llama-4-maverick or your alias)
#       embed: :8081 (qwen3-embedding-8b)
# 2b. Bring up Arango + bridge:
make up
make health

# 3. Tests
export ARANGO_TEST_RUN=1
# Optional: LLM-dependent integration tests
# export LLM_TEST_RUN=1
dotnet test dais-bridge.tests/dais-bridge.tests.csproj

# 4. Populate posts
npm run rag:reindex

# 5. Search smoke
curl -s -X POST http://localhost:5000/api/memory/search \
     -H 'content-type: application/json' \
     -d '{"query":"cast iron pan","k":3}'
```

## Environment quirks

- **Embedding config mismatch:** if you swap embedding models without running `/api/admin/migrate-embeddings`, the bridge throws `EmbeddingConfigMismatchException` on the first endpoint call. The exception's message includes the exact curl command to fix it.
- **Hardware:** AMD Ryzen AI Max+ 395 (Strix Halo) with unified memory. Don't reason about VRAM the way you would on a dGPU; chat + embed run simultaneously without VRAM partitioning concerns.
- **AQL bind var collision:** `@@col` is the AQL collection bind syntax (note the double `@`). Mixing it with `@col` for a string parameter is a classic typo — keep them distinct.
- **`overwrite=true` upsert:** the post upsert uses `?overwrite=true` on the document POST, which is ArangoDB's "insert or replace by _key" mode. PATCH semantics for partial-update aren't used because we always write the full doc.

## Common debugging entry points

| Symptom | Where to look |
|---|---|
| Reindex hangs | `:8081` (qwen3-embedding-8b) is probably down or RAM-pressured; check `top -p $(pgrep -fa qwen)` |
| Reindex 503 | Bridge can't reach embedding server; check `LLM_EMBEDDING_URL` env var in compose and `host.containers.internal:8081` from inside the bridge container (`podman exec` + curl) |
| Search returns empty | `memory_posts` collection is empty; run `npm run rag:reindex` |
| Mismatch exception | Run the curl in the exception message; restart bridge after migration completes |
| Tests time out | Check `ARANGO_TEST_RUN=1` is exported; integration tests skip silently otherwise but a timeout means Arango is unreachable |
```

- [ ] **Step 4: Commit**

```bash
git add HANDOFF.md CLAUDE.md docs/superpowers/RESUME-content-rag.md
git commit -m "$(cat <<'EOF'
docs: Phase 13 entry in HANDOFF + CLAUDE command table + resume guide

HANDOFF gets a brief Phase 13 section pointing at spec/plan/resume.
CLAUDE.md authoring-scripts command table gets rag:reindex. New
resume guide consolidates everything a cold-start session needs.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 14 — Self-review and merge prep

### Task 14.1: Final test sweep

- [ ] **Step 1: Run all unit + integration tests**

```bash
ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```
Expected: all tests pass. Compare count to baseline (29) plus the new tests added in this plan (target: ~50+ passing).

- [ ] **Step 2: Run JS test suite (for posts.mjs helpers we used)**

```bash
npm run test:scripts
```
Expected: existing 28 pass, no regressions.

- [ ] **Step 3: Run lint + format + type-check**

```bash
npm run check
npm run lint
```
Expected: no new errors introduced by this branch. Pre-existing lint debt (per CLAUDE.md tech-debt section) is OK to leave.

### Task 14.2: PR-readiness check

- [ ] **Step 1: Review branch commit log**

```bash
git log --oneline master..feature/content-rag
```
Expected: ~25-35 commits, each with a focused message. No "wip" or "fixup" commits.

- [ ] **Step 2: Diff summary**

```bash
git diff --stat master..feature/content-rag | tail -20
```
Expected: roughly 15-25 files modified, ~2000-3000 lines added (most in test files + the spec/plan docs).

- [ ] **Step 3: Confirm spec ↔ plan ↔ code alignment**

Walk through `docs/superpowers/specs/2026-05-16-content-rag-design.md` §2 (Goals) line by line. For each goal, point at the task in this plan that delivers it. Any unmatched goal is a gap to fix before opening the PR.

- [ ] **Step 4: Open PR (when ready)**

```bash
gh pr create --title "Content RAG + Phase 11 stack-drift fixes" --body "$(cat <<'EOF'
## Summary
- Adds `MemoryKind.Post` and a `memory_posts` collection; each post stored as two vectors (summary + body)
- New bridge endpoints: `POST /api/admin/reindex-posts`, `POST /api/memory/search`, `POST /api/admin/migrate-embeddings`
- New `scripts/rag-reindex.mjs` + `npm run rag:reindex`
- Resolves stack drift: LM Studio → llama.cpp on host, nomic 768-dim → qwen3 4096-dim, split chat/embed URLs
- `MemoryStore.EnsureSchemaAsync` shifted to lazy first-use so the migration endpoint stays reachable during config-mismatch states
- 25+ new tests across unit + integration layers, gated by `ARANGO_TEST_RUN=1` and (optionally) `LLM_TEST_RUN=1`

## Spec
[`docs/superpowers/specs/2026-05-16-content-rag-design.md`](docs/superpowers/specs/2026-05-16-content-rag-design.md)

## Test plan
- [ ] `ARANGO_TEST_RUN=1 dotnet test` — all pass
- [ ] `make up && npm run rag:reindex` — populates memory_posts (~26 vectors)
- [ ] `curl :5000/api/memory/search -d '{"query":"cast iron pan"}'` — returns top hits including what-we-pack-first-in-the-rv
- [ ] Second `rag:reindex` is all cache hits
- [ ] Editing one post body and re-running shows 2 embed + 24 cached
EOF
)"
```

---

## Self-review against the spec

(Author runs this before opening the PR. Not a runtime gate.)

| Spec §  | Coverage |
|---|---|
| §1 Problem | n/a |
| §2 Goal 1 (ingest) | Phase 6 (`UpsertPostAsync`), Phase 7 (stale deletion), Phase 11 (script) |
| §2 Goal 2 (retrieve) | Phase 8 (`SearchAsync`), Phase 10.3 (search endpoint) |
| §2 Goal 3 (layer on MemoryStore) | All Phase 2-9 work extends the same class |
| §2 Goal 4 (adapt stack) | Phase 1 (env vars, appsettings, compose, .env), Phase 0.2 (rename) |
| §2 Goal 5 (schema-version safety) | Phase 3 (sentinel + mismatch), Phase 4 (lazy bootstrap), Phase 9 (migration), Phase 10.4 (endpoint) |
| §2 Goal 6 (testable) | All phases include unit + integration tests; gates documented in §9 of the spec |
| §3 Non-goals | Honored: no kernel plugin, no SignalR integration, no chunking, no background worker, no auth |
| §4 Architecture | Diagram and three path narratives align with what's built |
| §5.1 Stack-drift config | Phase 1.1 (Program.cs), 1.2 (appsettings), 1.3 (compose), 1.4 (.env) |
| §5.2 Rename | Phase 0.2 |
| §5.3 `MemoryKind`/`MemoryCollections` | Phase 2.1 |
| §5.4 `memory_meta` collection | Phase 2.2 (creation), Phase 3 (sentinel logic) |
| §5.5 Post document shape | Phase 5 (records), Phase 6 (writer) |
| §5.6 `MemoryStore` new methods | UpsertPostAsync (Phase 6), DeleteStalePostsAsync (Phase 7), SearchAsync (Phase 8), MigrateEmbeddingsAsync (Phase 9) |
| §5.7 HTTP endpoints | Phase 10.1 (reindex), 10.3 (search), 10.4 (migrate) |
| §5.8 `rag-reindex.mjs` | Phase 11.2 |
| §5.9 (optional) `rag-search.mjs` | Intentionally deferred — not in this plan |
| §6.1 Text composition | Phase 5.2 (composer + 7 tests) |
| §6.2 Retrieval AQL | Phase 8.1 |
| §6.3 Application dedup | Phase 10.3 (in `HandleSearchAsync`) |
| §7 Data flow | Verified end-to-end in Phase 12 (smoke) |
| §8 Error handling | Each handler in Phase 10 maps exceptions to the response shapes in the spec's table |
| §9 Testing | 25-test target hit across the integration test classes added in Phases 2-10 |
| §10 Open gaps | Acknowledged in plan but not addressed — by design |
| §11 Decisions log | Driving every task; no plan-level deviations from spec decisions |

No gaps identified. Plan is ready.
