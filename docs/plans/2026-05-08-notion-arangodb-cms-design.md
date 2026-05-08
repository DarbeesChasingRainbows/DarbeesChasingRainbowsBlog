# Notion CMS + ArangoDB Knowledge Graph Design

## Overview
This design outlines the transition from a local Deno Fresh/Directus CMS to an all-in-one Notion-based authoring environment. The new architecture serves as a primary ingestion node for a persistent architectural intelligence system (powered by a local ArangoDB knowledge graph) hosted on a new Linux desktop.

The goal is to capture the author's intent and lived experience seamlessly, removing clunky MDX component authoring while providing active, knowledge-aware feedback during the writing process.

## Architecture

### 1. The Authoring Experience (Notion)
- **Separate Databases:** The content is organized into four separate Notion databases (Blog, Books, Projects, Field Notes) to maintain clean properties and allow for future expansion.
- **Native Block Usage:** Prose is written using Notion's native blocks. Complex components (Carousels, Callouts, Galleries) are created using standard Notion blocks (e.g., a native Notion Gallery).
- **Semantic Mapping:** The system relies on semantic mapping. A native Notion block is automatically translated into the corresponding Astro component (e.g., a Notion Callout becomes an Astro `<Callout>`) by the ingestion script.

### 2. The Linux Bridge (Ingestion & Sync)
A local service running on the Linux desktop acts as the orchestrator.
- **Trigger:** The author updates a "Status" property in Notion (e.g., `🧠 Brainstorm`, `💬 Feedback`, `✨ Optimize`, `🚀 Published`).
- **Data Fetching:** The script pulls the page content via the Notion API.
- **Cloudflare Image Pipeline:** 
  - To bypass Notion's 1-hour image URL expiry, the script downloads all images found in the Notion document.
  - Images are uploaded to Cloudflare Images (or R2).
  - The Notion page is updated (or the local Astro build mapping is updated) to point to the permanent Cloudflare URLs, ensuring the Astro site builds without relying on transient links.

### 3. The Knowledge Graph Feedback Loop (ArangoDB + LM Studio)
The CMS is no longer just a publishing tool; it is a bidirectional interface with the family/code knowledge graph.

- **Brainstorming:** Setting status to `🧠 Brainstorm` triggers local LM Studio to generate an outline and identify necessary entities based on the core intent.
- **Writing & Feedback (`💬 Feedback`):** The bridge queries the ArangoDB knowledge graph against the current draft. It provides a "Graph-Driven Insight" block at the top of the Notion page, highlighting:
  - Missing connections (e.g., "You discussed soil health; link this to 'Regenerative Principles'").
  - Historical evolution of thought based on previous entries.
  - Architectural or principle alignments.
- **Optimization (`✨ Optimize`):** Once writing is complete, the bridge extracts GEO/SEO metadata (`aiSummary`, `keyTakeaways`, `entityMentions`, `faq`) and populates them into Notion properties for a final human review.

## Data Flow
1. **Authoring:** Notion UI -> Native Blocks.
2. **Sync/Bridge:** Status Change -> Local Linux Script.
3. **Image Processing:** Notion -> Local Download -> Cloudflare Images -> Permanent URL.
4. **Knowledge Processing:** Script -> ArangoDB (Context) -> LM Studio (Inference) -> Notion (Feedback/Metadata).
5. **Publishing:** Status `🚀 Published` -> Astro Content Loader pulls structured data and translated MDX blocks for the static build.

## Key Benefits
- **Zero MDX Friction:** The author never touches JSX or raw MDX for components.
- **Persistent Intelligence:** Every post feeds the ArangoDB graph, and the graph actively coaches the authoring process.
- **Future-Proof Infrastructure:** Cloudflare handles image longevity, and Linux provides a robust environment for local AI and database hosting.
