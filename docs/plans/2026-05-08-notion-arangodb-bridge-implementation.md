# Notion + ArangoDB CMS Bridge Implementation Plan

> **For Gemini:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a Linux-based bridge that syncs Notion content to an ArangoDB knowledge graph, processes images through Cloudflare, and enriches posts with local AI.

**Architecture:** A local TypeScript/Deno orchestrator that watches Notion for status changes. It performs a "Cloudflare Wash" on images, queries ArangoDB for contextual feedback, and triggers LM Studio for GEO metadata generation before updating Notion and emitting clean data for the Astro build.

**Tech Stack:** Deno (runtime), Notion API, ArangoDB (arangojs), Cloudflare Images API, LM Studio (OpenAI API compatible).

---

### Task 1: Environment & Client Setup

**Files:**
- Create: `bridge/deno.json`
- Create: `bridge/.env.example`
- Create: `bridge/lib/notion.ts`

**Step 1: Create Deno configuration**

```json
{
  "tasks": {
    "dev": "deno run --allow-net --allow-read --allow-env --watch main.ts",
    "sync": "deno run --allow-net --allow-read --allow-write --allow-env main.ts"
  },
  "imports": {
    "@notionhq/client": "https://deno.land/x/notion_sdk@v2.2.3/src/mod.ts",
    "std/": "https://deno.land/std@0.208.0/"
  }
}
```

**Step 2: Initialize .env.example**

```text
NOTION_TOKEN=
NOTION_BLOG_DB_ID=
NOTION_BOOKS_DB_ID=
NOTION_PROJECTS_DB_ID=
NOTION_NOTES_DB_ID=
CLOUDFLARE_API_TOKEN=
CLOUDFLARE_ACCOUNT_ID=
ARANGODB_URL=http://localhost:8529
ARANGODB_DB=darbees_knowledge
LM_STUDIO_URL=http://localhost:1234/v1
```

**Step 3: Implement Notion client wrapper**

```typescript
import { Client } from "@notionhq/client";
import { load } from "std/dotenv/mod.ts";

const env = await load();
export const notion = new Client({ auth: env["NOTION_TOKEN"] });

export async function getPagesByStatus(databaseId: string, status: string) {
  return await notion.databases.query({
    database_id: databaseId,
    filter: { property: "Status", status: { equals: status } },
  });
}
```

**Step 4: Commit**

```bash
git add bridge/deno.json bridge/.env.example bridge/lib/notion.ts
git commit -m "chore: initialize bridge environment and notion client"
```

---

### Task 2: Cloudflare Image "Wash" Pipeline

**Files:**
- Create: `bridge/lib/cloudflare.ts`
- Modify: `bridge/main.ts`

**Step 1: Implement Cloudflare Upload**

```typescript
export async function uploadToCloudflare(url: string, filename: string) {
  // 1. Download Notion file
  const res = await fetch(url);
  const blob = await res.blob();

  // 2. Upload to Cloudflare Images API
  const formData = new FormData();
  formData.append("file", blob, filename);
  
  const cfRes = await fetch(`https://api.cloudflare.com/client/v4/accounts/${ACCOUNT_ID}/images/v1`, {
    method: "POST",
    headers: { Authorization: `Bearer ${TOKEN}` },
    body: formData
  });
  const data = await cfRes.json();
  return data.result.variants[0]; // Return permanent URL
}
```

**Step 2: Commit**

```bash
git add bridge/lib/cloudflare.ts
git commit -m "feat: add cloudflare image upload utility"
```

---

### Task 3: ArangoDB Knowledge Integration

**Files:**
- Create: `bridge/lib/arango.ts`

**Step 1: Implement Graph Query for Feedback**

```typescript
import { Database } from "https://esm.sh/arangojs@8.1.0";

const db = new Database({ url: "http://localhost:8529" });

export async function getContextualSuggestions(text: string) {
  // Simple entity matching against ArangoDB collections
  const entities = await db.query(`
    FOR e IN Entities
    FILTER CONTAINS(@text, e.name)
    RETURN e
  `, { text });
  return await entities.all();
}
```

**Step 2: Commit**

```bash
git add bridge/lib/arango.ts
git commit -m "feat: implement basic ArangoDB entity lookup"
```

---

### Task 4: Block Translation Engine (Notion -> Astro MDX)

**Files:**
- Create: `bridge/lib/translator.ts`

**Step 1: Map Notion Blocks to Astro Components**

```typescript
export function translateBlock(block: any): string {
  switch (block.type) {
    case "paragraph": return block.paragraph.rich_text.map(t => t.plain_text).join("");
    case "callout": 
      return `<Callout>${block.callout.rich_text.map(t => t.plain_text).join("")}</Callout>`;
    case "image":
      return `![${block.image.caption[0]?.plain_text || ""}](${block.image.file.url})`;
    // ... handle gallery to <ImageGallery />
    default: return "";
  }
}
```

**Step 2: Commit**

```bash
git add bridge/lib/translator.ts
git commit -m "feat: implement core Notion-to-MDX block translator"
```

---

### Task 5: AI Enrichment (LM Studio)

**Files:**
- Create: `bridge/lib/ai.ts`

**Step 1: Implement GEO Metadata Generation**

```typescript
export async function generateGeoMetadata(content: string) {
  const res = await fetch("http://localhost:1234/v1/chat/completions", {
    method: "POST",
    body: JSON.stringify({
      model: "local-model",
      messages: [{ role: "system", content: "You are a GEO expert..." }, { role: "user", content }]
    })
  });
  const data = await res.json();
  return JSON.parse(data.choices[0].message.content); // Expecting keyTakeaways, aiSummary, etc.
}
```

**Step 2: Commit**

```bash
git add bridge/lib/ai.ts
git commit -m "feat: add LM Studio enrichment loop"
```

---

### Task 6: Astro Retrofit (The Notion Loader)

**Files:**
- Modify: `src/content.config.ts`

**Step 1: Update Astro to pull from local JSON/MDX emitted by bridge**

```typescript
const blog = defineCollection({
  loader: glob({ pattern: "**/*.mdx", base: "./src/content/blog" }),
  schema: blogSchema
});
```

**Step 2: Commit**

```bash
git add src/content.config.ts
git commit -m "refactor: update content loader to support bridge-emitted files"
```
