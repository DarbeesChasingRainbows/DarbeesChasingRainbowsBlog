# ArangoDB → SurrealDB Migration Hand-off

## Status: Phase 1 Complete (Hexagonal Architecture Refactor)
The project has been successfully refactored to a Hexagonal Architecture (Ports & Adapters). Infrastructure concerns (ArangoDB/SurrealDB) are now strictly separated from the Domain logic. All components now use rich Value Objects and depend on Port Interfaces.

### **What was Completed**

1.  **Pure Domain Layer Establishment**:
    *   `dais-bridge/Domain/`: New root for all dependency-free logic.
    *   `ValueObjects/`: `TenantId`, `EmbeddingVector`, and `ContentHash` (replaces primitive strings/floats with validated types).
    *   `Ports/`: `IMemoryRepository`, `ISchemaManager`, `IRecallEngine`, `IEntityExtractor`, `IDomainEventDispatcher`.
    *   `Events/`: Observer pattern implementation for real-time visibility (`PostEmbeddedEvent`, etc.).
    *   `Services/`: Pure logic like `PostTextComposer`.

2.  **Infrastructure Realignment**:
    *   `dais-bridge/Infrastructure/Arango/`: Moved and renamed `MemoryStore` → `ArangoMemoryRepository`.
    *   `dais-bridge/Infrastructure/SurrealDb/`: Updated `SurrealDbMemoryRepository` to implement the new ports and use Value Objects.
    *   `dais-bridge/Infrastructure/Embedding/`: Home for `OpenAiCompatibleEmbeddingClient`.

3.  **Application Layer DI Wiring**:
    *   `Program.cs`: Now wires backend-specific implementations to generic ports based on `MEMORY_BACKEND` ("arango" or "surreal").
    *   `MemoryPlugin` & `ContentRagEndpoints`: Now strictly depend on `IMemoryRepository` and `IRecallEngine` interfaces.

4.  **Test Suite Modernization**:
    *   Updated all integration and contract tests to use `TenantId` and the new port structure.
    *   Tests now run against whichever backend is configured, with `ArangoMemoryRepositoryContractTests` and `SurrealDbMemoryRepositoryContractTests` ensuring Liskov Substitution.

5.  **Tooling Support**:
    *   `scripts/lib/surreal-client.mjs`: New minimal client for Node.js scripts.
    *   `scripts/related-rebuild.mjs`: Updated to support both backends.

---

### **Next Steps to Start**

1.  **Phase 2: Finalize SurrealDB Implementation**
    *   Verify all SurrealQL queries in `SurrealDbMemoryRepository.cs`.
    *   Ensure Graph Traversal (`->memory_edges->target`) logic in `SurrealDbRecallEngine` correctly handles 1..N hops (iteratively if needed).
    *   Validate `EnsureSchemaAsync` against a fresh SurrealDB instance.

2.  **Phase 3: Integration Verification**
    *   Start SurrealDB via Podman/Docker: `docker-compose up surrealdb`.
    *   Run integration tests: `SURREAL_TEST_RUN=1 dotnet test dais-bridge.tests/`.
    *   Verify `npm run rag:reindex` works when `MEMORY_BACKEND=surreal`.

3.  **Phase 4: Cleanup & Deletion**
    *   Once SurrealDB is verified as the production-ready primary, remove `Infrastructure/Arango/`.
    *   Remove `ArangoDBNetStandard` NuGet package.
    *   Delete `scripts/lib/arango-client.mjs`.

4.  **Future (Optional): F# Migration**
    *   The `Domain/` layer is now mechanically portable to F#. Consider moving it to a `.fsproj` to leverage Discriminated Unions for "making illegal states unrepresentable."

### **Key Files for Reference**
*   `dais-bridge/Domain/Ports/IMemoryRepository.cs`: The core persistence contract.
*   `dais-bridge/Program.cs`: The Composition Root where the backend is chosen.
*   `dais-bridge/Infrastructure/SurrealDb/SurrealDbMemoryRepository.cs`: The target implementation.
