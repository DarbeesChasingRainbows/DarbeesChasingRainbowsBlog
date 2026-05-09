# Sovereign Gateway Foundation Implementation Plan

> **For Gemini:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Convert the DAIS Console App into a unified .NET 9 Minimal API gateway with SignalR and deterministic safety pipelines.

**Architecture:** ASP.NET Core Minimal API acting as a "Librarian Supervisor." It uses custom C# middleware for deterministic safety filtering before dispatching to a multi-tenant Semantic Kernel orchestrator.

**Tech Stack:** .NET 9, ASP.NET Core, SignalR, Microsoft.SemanticKernel (v1.75+), ArangoDBNetStandard, YamlDotNet.

---

### Task 1: API Refactoring & Web Hosting

**Files:**
- Modify: `dais-bridge/dais-bridge.csproj`
- Modify: `dais-bridge/Program.cs`
- Create: `dais-bridge/Middleware/SafetyMiddleware.cs`

**Step 1: Update Project to Web SDK**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Darbee.Gateway</RootNamespace>
  </PropertyGroup>
  <!-- Keep existing ItemGroups -->
</Project>
```

**Step 2: Initialize WebApplication in Program.cs**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
// Add existing DI registrations for Plugins
var app = builder.Build();
app.MapGet("/", () => "Darbee Sovereign AI Gateway Active");
app.Run();
```

**Step 3: Commit**

```bash
git add dais-bridge/dais-bridge.csproj dais-bridge/Program.cs
git commit -m "refactor: convert console app to ASP.NET Core Minimal API"
```

---

### Task 2: Deterministic Safety Middleware

**Files:**
- Create: `dais-bridge/Middleware/SafetyMiddleware.cs`
- Create: `dais-bridge/Models/SafetyPolicies.cs`
- Create: `dais-bridge/safety_policies.json`

**Step 1: Write failing test for safety block**

```csharp
[Fact]
public async Task Middleware_ShouldBlockUnsafeContent() {
    // Arrange: Mock request with "guns"
    // Act: Invoke middleware
    // Assert: Check response is 400 or blocked status
}
```

**Step 2: Implement SafetyMiddleware logic**

```csharp
public class SafetyMiddleware {
    private readonly RequestDelegate _next;
    public async Task InvokeAsync(HttpContext context) {
        // Read body, check against safety_policies.json
        // If fail: return "Refusal" + alert parent SignalR
        // If pass: await _next(context)
    }
}
```

**Step 3: Commit**

```bash
git add dais-bridge/Middleware/ dais-bridge/safety_policies.json
git commit -m "feat: add deterministic safety middleware"
```

---

### Task 3: SignalR Hubs (KidSafe & Parent)

**Files:**
- Create: `dais-bridge/Hubs/KidSafeHub.cs`
- Create: `dais-bridge/Hubs/ParentHub.cs`

**Step 1: Implement KidSafeHub for voice/text streaming**

```csharp
public class KidSafeHub : Hub {
    public async Task SendMessage(string user, string message) {
        // Route to Semantic Kernel with Safety check
    }
}
```

**Step 2: Implement ParentHub for real-time alerts**

```csharp
public class ParentHub : Hub {
    // Methods for parent to approve/deny legacy nodes
}
```

**Step 3: Commit**

```bash
git add dais-bridge/Hubs/
git commit -m "feat: implement SignalR hubs for KidSafe and Parent Dashboard"
```

---

### Task 4: Tenant-Aware ArangoDB Integration

**Files:**
- Modify: `dais-bridge/Plugins/ArangoPlugin.cs`
- Create: `dais-bridge/Models/TenantContext.cs`

**Step 1: Update ArangoPlugin to enforce filters**

```csharp
public async Task<string> QueryGraph(string aql, string tenantId) {
    var scopedAql = $"{aql} FILTER doc.tenant_id == \"{tenantId}\"";
    // Execute...
}
```

**Step 2: Commit**

```bash
git add dais-bridge/Plugins/ArangoPlugin.cs
git commit -m "feat: implement tenant isolation in ArangoDB plugin"
```

---

### Task 5: Full Integration & Multi-Tenant Kernel

**Files:**
- Modify: `dais-bridge/Program.cs`

**Step 1: Wire Hubs and Middleware**

```csharp
app.UseMiddleware<SafetyMiddleware>();
app.MapHub<KidSafeHub>("/hubs/kidsafe");
app.MapHub<ParentHub>("/hubs/parent");
```

**Step 2: Commit**

```bash
git add dais-bridge/Program.cs
git commit -m "feat: finalize gateway integration"
```

---

### Task 6: External Knowledge Plugin (ResearchPlugin via Context7 MCP)

**Files:**
- Create: `dais-bridge/Plugins/IMcpToolClient.cs`
- Create: `dais-bridge/Plugins/Context7McpToolClient.cs`
- Modify: `dais-bridge/Plugins/ResearchPlugin.cs`
- Modify: `dais-bridge/Program.cs`
- Modify: `dais-bridge.tests/ResearchPluginTests.cs`

**Brief:** A Semantic Kernel plugin that lets the LLM look up live library/framework documentation by calling the Context7 MCP server (`resolve-library-id` → `query-docs`). Added because grounding answers in current docs is materially better than relying on training-data recall, especially for fast-moving libraries (Astro, SK, etc.).

**Threat model:**
- **Outbound MCP egress carries LLM-controlled strings.** A child could (in theory) coax the model into emitting sensitive content as `libraryName` or `query`, which would be transmitted to a third-party endpoint.
- **Mitigation (i):** ResearchPlugin is excluded from `kernel-kidsafe` and is only available via `kernel-admin` (ParentHub / management surfaces). Children's chat cannot reach Context7 by construction.
- **Mitigation (ii):** Inputs are length-limited to 200 chars and validated at function entry; oversized/empty arguments return a graceful error string without making any MCP call.
- **Mitigation (iii):** An outbound `IFunctionInvocationFilter` is recommended as future hardening to log/scrub plugin arguments before egress, even on the admin kernel.
- **Per-call MCP client lifecycle** is accepted as the initial design — connection pooling, timeouts, retries, and circuit-breaker hardening are deferred to a follow-up.

**Status:** Implemented in commit `49cb6fb`; hardened in the follow-up commit covering the three Critical findings (off-plan, trust-boundary singleton, brittle test).
