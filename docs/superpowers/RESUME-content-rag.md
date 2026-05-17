# Resume Guide — Content RAG

> **Purpose:** Everything you (or a future agent session) need to pick this work back up cold without re-deriving context.

**Last updated:** 2026-05-17
**Branch:** `feature/content-rag` (off `master`)
**Status:** Spec + plan committed; implementation complete. See the spec/plan for architecture and TDD steps; this guide is for cold-start setup and troubleshooting.

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

# 2c. ONE-TIME: create the darbees_knowledge database
#     (the bridge doesn't auto-create databases yet — known gap)
curl -s -X POST -u root:$(grep ^ARANGO_ROOT_PASSWORD .env | cut -d= -f2) \
     -H "content-type: application/json" \
     -d '{"name":"darbees_knowledge"}' \
     http://localhost:8529/_api/database

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
| Reindex 500, "database not found" | One-time DB creation hasn't been done; see Quick Start step 2c |
| Search returns empty | `memory_posts` collection is empty; run `npm run rag:reindex` |
| Mismatch exception | Run the curl in the exception message; restart bridge after migration completes |
| Tests time out | Check `ARANGO_TEST_RUN=1` is exported; integration tests skip silently otherwise but a timeout means Arango is unreachable |

## Known gaps / follow-ups (not yet addressed)

1. **Auto-create database:** the bridge assumes `darbees_knowledge` exists; first-boot requires a manual curl. Adding `EnsureDatabaseAsync` to `MemoryStore` is a clean follow-up.
2. **Pending-embeddings worker:** `MigrateEmbeddingsAsync` enqueues docs but nothing drains the queue for chat memory. Posts can be re-ingested by running `npm run rag:reindex --force`; chat memory needs a `BackgroundService` (Phase 11 C/D scope).
3. **Auth on admin endpoints:** `/api/admin/reindex-posts`, `/api/memory/search`, and `/api/admin/migrate-embeddings` are unauthenticated. Bridge is bound to local Podman networking + host loopback, but adding hub-tenant-gating is required before any non-local deployment.
4. **Spurious commits in branch history:** `38bf089` "DAIS: Test commit" is a `git mv`-only commit (no line changes — the actual rename content is in `4014a80`). And `adc67ac` is an empty commit. Both should be squashed/dropped before PR merge (`git rebase -i`).
