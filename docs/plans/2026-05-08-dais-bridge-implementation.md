# DAIS: C# Semantic Kernel Bridge Implementation Plan

> **For Gemini:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a .NET 9 background service using Microsoft Semantic Kernel to orchestrate the flow between Obsidian, ArangoDB, and Astro, including autonomous Git publishing.

**Architecture:** A Hexagonal (Ports & Adapters) C# service. It uses Native SK Plugins for file I/O, ArangoDB AQL querying, and local AI inference. It leverages Automatic Function Calling to process notes, classify images, and grow the knowledge graph.

**Tech Stack:** .NET 9, Microsoft.SemanticKernel (v1.30+), ArangoDBNetStandard, YAMLDotNet, Cloudflare Images API, LM Studio, Git CLI.

---

### Task 1: Environment & Project Scaffolding

**Files:**
- Create: `dais-bridge/DAIS.Bridge.csproj`
- Create: `dais-bridge/appsettings.json`
- Create: `dais-bridge/Program.cs`

**Step 1: Create the .NET Console Project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.SemanticKernel" Version="1.30.0" />
    <PackageReference Include="ArangoDBNetStandard" Version="2.0.0" />
    <PackageReference Include="YamlDotNet" Version="16.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
  </ItemGroup>
</Project>
```

---

### Task 2: The Obsidian Plugin (Markdown Port)

**Files:**
- Create: `dais-bridge/Plugins/ObsidianPlugin.cs`

**Step 1: Implement Read/Write with Frontmatter support**

```csharp
public class ObsidianPlugin {
    [KernelFunction, Description("Reads an Obsidian note and parses YAML frontmatter.")]
    public async Task<string> GetNoteContent(string slug) { ... }

    [KernelFunction, Description("Updates the YAML frontmatter of an Obsidian note.")]
    public async Task UpdateNoteMetadata(string slug, string yamlJson) { ... }
}
```

---

### Task 3: The ArangoDB Plugin (Graph Port)

**Files:**
- Create: `dais-bridge/Plugins/ArangoPlugin.cs`

**Step 1: Implement Knowledge Graph connectivity**

```csharp
public class ArangoPlugin {
    [KernelFunction, Description("Queries ArangoDB for related entities using AQL.")]
    public async Task<string> QueryGraph(string aql) { ... }

    [KernelFunction, Description("Creates a new knowledge node in ArangoDB.")]
    public async Task CreateNode(string collection, string jsonData) { ... }
}
```

---

### Task 4: The Asset & GEO Plugins

**Files:**
- Create: `dais-bridge/Plugins/AssetPlugin.cs`
- Create: `dais-bridge/Plugins/GEOPlugin.cs`

**Step 1: Implement Image Classification & GEO Metadata Generation**

```csharp
public class AssetPlugin {
    [KernelFunction, Description("Uploads a local image to Cloudflare and returns permanent URL.")]
    public async Task<string> WashImage(string localPath) { ... }
}

public class GEOPlugin {
    [KernelFunction, Description("Generates GEO/SEO metadata (Summary, FAQ, Takeaways).")]
    public async Task<string> GenerateMetadata(string content) { ... }
}
```

---

### Task 5: Core Orchestration & Git Integration

**Files:**
- Modify: `dais-bridge/Program.cs`
- Create: `dais-bridge/Plugins/GitPlugin.cs`

**Step 1: Implement the Git Plugin for Granular Commits**

```csharp
public class GitPlugin {
    [KernelFunction, Description("Stages and commits a specific file with a message.")]
    public async Task StageAndCommit(string filePath, string message) {
        // Run: git add {filePath}
        // Run: git commit -m "{message}"
    }

    [KernelFunction, Description("Pushes all local commits to the remote repository.")]
    public async Task PushChanges() {
        // Run: git push
    }
}
```

**Step 2: Wire up Kernel with v1.30+ Function Choice Behavior**

```csharp
var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion(modelId, lmStudioUrl); // Compatible API
builder.Plugins.AddFromType<ObsidianPlugin>();
builder.Plugins.AddFromType<ArangoPlugin>();
builder.Plugins.AddFromType<AssetPlugin>();
builder.Plugins.AddFromType<GEOPlugin>();
builder.Plugins.AddFromType<GitPlugin>();

var kernel = builder.Build();

OpenAIPromptExecutionSettings settings = new() { 
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() 
};

await kernel.InvokePromptAsync("Process all notes with status '✨ Process', emit MDX, and commit/push every file.", new(settings));
```

---

### Task 6: Astro Integration & GitHub Actions

**Files:**
- Modify: `astro.config.mjs`
- Create: `.github/workflows/deploy.yml`

**Step 1: Add Cloudflare Adapter to Astro**

```js
import cloudflare from '@astrojs/cloudflare';
export default defineConfig({
  adapter: cloudflare(),
  // ...
});
```

**Step 2: Create GitHub Action for Cloudflare Pages**

```yaml
name: Deploy to Cloudflare Pages
on: [push]
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - run: npm install
      - run: npm run build
      - uses: cloudflare/pages-action@v1
        with:
          apiToken: ${{ secrets.CLOUDFLARE_API_TOKEN }}
          accountId: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}
          projectName: 'darbees-blog'
          directory: 'dist'
```
