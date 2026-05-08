# DAIS: C# Semantic Kernel Bridge Implementation Plan

> **For Gemini:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a .NET 9 background service using Microsoft Semantic Kernel to orchestrate the flow between Obsidian, ArangoDB, and Astro.

**Architecture:** A Hexagonal (Ports & Adapters) C# service. It uses Native SK Plugins for file I/O, ArangoDB AQL querying, and local AI inference. It leverages Automatic Function Calling to process notes, classify images, and grow the knowledge graph.

**Tech Stack:** .NET 9, Microsoft.SemanticKernel (v1.30+), ArangoDBNetStandard, YAMLDotNet, Cloudflare Images API, LM Studio.

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
    <Nullable>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.SemanticKernel" Version="1.30.0" />
    <PackageReference Include="ArangoDBNetStandard" Version="2.0.0" />
    <PackageReference Include="YamlDotNet" Version="16.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
  </ItemGroup>
</Project>
```

**Step 2: Initialize Configuration**

```json
{
  "Obsidian": {
    "VaultPath": "/home/user/obsidian/darbees-vault",
    "BlocksPath": "_blocks"
  },
  "ArangoDB": {
    "Url": "http://localhost:8529",
    "Database": "darbees_knowledge",
    "User": "root",
    "Password": "password"
  },
  "AI": {
    "LMStudioUrl": "http://localhost:1234/v1",
    "ModelId": "local-model"
  },
  "Cloudflare": {
    "AccountId": "YOUR_ID",
    "ApiToken": "YOUR_TOKEN"
  }
}
```

**Step 3: Commit**

```bash
git add dais-bridge/
git commit -m "chore: scaffold .NET DAIS bridge project"
```

---

### Task 2: The Obsidian Plugin (Markdown Port)

**Files:**
- Create: `dais-bridge/Plugins/ObsidianPlugin.cs`

**Step 1: Implement Read/Write with Frontmatter support**

```csharp
public class ObsidianPlugin {
    [KernelFunction, Description("Reads an Obsidian note and parses YAML frontmatter.")]
    public async Task<string> GetNoteContent(string slug) {
        // Logic to find file, split YAML from Body using YamlDotNet
    }

    [KernelFunction, Description("Updates the YAML frontmatter of an Obsidian note.")]
    public async Task UpdateNoteMetadata(string slug, string yamlJson) {
        // Logic to re-serialize YAML and write back to .md file
    }
}
```

**Step 2: Commit**

```bash
git add dais-bridge/Plugins/ObsidianPlugin.cs
git commit -m "feat: add Obsidian plugin for markdown/YAML operations"
```

---

### Task 3: The ArangoDB Plugin (Graph Port)

**Files:**
- Create: `dais-bridge/Plugins/ArangoPlugin.cs`

**Step 1: Implement Knowledge Graph connectivity**

```csharp
public class ArangoPlugin {
    [KernelFunction, Description("Queries ArangoDB for related entities using AQL.")]
    public async Task<string> QueryGraph(string aql) {
        // Logic using ArangoDBNetStandard to run queries
    }

    [KernelFunction, Description("Creates a new knowledge node in ArangoDB.")]
    public async Task CreateNode(string collection, string jsonData) {
        // Logic to upsert document
    }
}
```

**Step 2: Commit**

```bash
git add dais-bridge/Plugins/ArangoPlugin.cs
git commit -m "feat: add ArangoDB plugin for knowledge graph integration"
```

---

### Task 4: The Intelligence & Asset Plugin

**Files:**
- Create: `dais-bridge/Plugins/AssetPlugin.cs`
- Create: `dais-bridge/Plugins/GEOPlugin.cs`

**Step 1: Implement Image Classification & Cloudflare Wash**

```csharp
public class AssetPlugin {
    [KernelFunction, Description("Uploads a local image to Cloudflare and returns permanent URL.")]
    public async Task<string> WashImage(string localPath) { ... }

    [KernelFunction, Description("Classifies an image using local vision model.")]
    public async Task<string> ClassifyLivedExperience(string localPath) { ... }
}
```

**Step 2: Implement GEO Metadata Generation**

```csharp
public class GEOPlugin {
    [KernelFunction, Description("Generates GEO/SEO metadata (Summary, FAQ, Takeaways).")]
    public async Task<string> GenerateMetadata(string content) { ... }
}
```

**Step 3: Commit**

```bash
git add dais-bridge/Plugins/AssetPlugin.cs dais-bridge/Plugins/GEOPlugin.cs
git commit -m "feat: add asset washing and GEO intelligence plugins"
```

---

### Task 5: Core Orchestration (The Semantic Loop)

**Files:**
- Modify: `dais-bridge/Program.cs`

**Step 1: Wire up Kernel with Automatic Function Calling**

```csharp
var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion(modelId, lmStudioUrl); // Compatible API
builder.Plugins.AddFromType<ObsidianPlugin>();
builder.Plugins.AddFromType<ArangoPlugin>();
builder.Plugins.AddFromType<AssetPlugin>();
builder.Plugins.AddFromType<GEOPlugin>();

var kernel = builder.Build();

// The Main Loop
var settings = new OpenAIPromptExecutionSettings { 
    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions 
};

await kernel.InvokePromptAsync("Find all Obsidian notes with status '✨ Process' and run the full sync pipeline.", new(settings));
```

**Step 2: Commit**

```bash
git add dais-bridge/Program.cs
git commit -m "feat: implement main semantic orchestration loop"
```

---

### Task 6: Astro Loader Integration

**Files:**
- Create: `src/utils/dais-loader.ts`
- Modify: `src/content.config.ts`

**Step 1: Create utility to read bridge-emitted MDX**

**Step 2: Commit**

```bash
git add src/utils/dais-loader.ts src/content.config.ts
git commit -m "refactor: integrate Astro with DAIS bridge output"
```
