# Todo - Phase 11: Graph-Backed RAG (Phase B)

- [ ] **B2** — `MemoryPlugin` kernel functions (`RememberDecision`, `RememberObservation`, `LinkMemory`). Tenant ID read from `ITenantContextAccessor`, never an LLM-bound parameter. ArangoDB required.
- [ ] **B3** — Replace `ArangoPlugin` registration with `MemoryPlugin` in `Program.cs`; delete `dais-bridge/Plugins/ArangoPlugin.cs` and `dais-bridge.tests/ArangoPluginTests.cs`.
- [ ] **B4** — Hubs (`KidSafeHub`, `ParentHub`) set `TenantContext` on `OnConnectedAsync` and on each method invocation.
- [ ] **B5** — Cross-tenant isolation integration test.
