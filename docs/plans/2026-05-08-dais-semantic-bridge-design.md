# Darbees Architectural Intelligence System (DAIS) Design Specification

## Overview
DAIS is a local-first "Librarian Agent" built to manage the persistent architectural intelligence of the Darbees family. It transitions the CMS from a standard cloud-based or script-based workflow to an agentic system that understands the user's lived experience through a bidirectional link between an Obsidian vault and an ArangoDB knowledge graph.

## Core Architecture

### 1. The Authoring Interface (Obsidian)
- **Primary Source:** Pure Markdown (`.md`) files stored locally on a Linux desktop.
- **Visual Components:** Complex Astro components (Carousels, Galleries) are managed via the **Obsidian Database Folder** plugin. Users reference them using simple wiki-links: `[[carousel:van-solar-build]]`.
- **Lived Experience Marking:** Every image block in the database has an `AI Classify` toggle. When checked, the system treats the photo as part of the family's "Knowledge Base" (e.g., an actual build photo vs. a stock reference).

### 2. The Semantic Engine (C# + .NET 9)
The engine is a background service built using **Microsoft Semantic Kernel**. It operates using **Automatic Function Calling**, allowing an LLM to orchestrate tools based on intent.

- **Native Plugins:**
    - `ObsidianPlugin`: Reads/Writes Markdown and manages YAML frontmatter injection.
    - `ArangoPlugin`: Executes AQL queries to find historical context and stores new "Intelligence Nodes."
    - `AssetPlugin`: Processes images. It "washes" local vault images into **Cloudflare Images** for permanent hosting and runs local vision models (LLaVA) for entity extraction.
    - `GEOPlugin`: Orchestrates **LM Studio** to generate citable GEO/SEO metadata.

### 3. The Knowledge Graph (ArangoDB)
- **Reference-Only Model:** ArangoDB does not duplicate the prose; it stores nodes representing Articles, Images, and Entities (e.g., "Victron Inverter," "Regenerative Principles") and links them with edges.
- **Agentic Memory:** Semantic Kernel uses ArangoDB as a vector store (via custom connector) to provide the LLM with long-term memory of previous decisions and builds.

### 4. The Publishing Pipeline (Astro)
- **Astro Loader:** The bridge emits "clean" `.mdx` files into the Astro `src/content` folder. 
- **Automatic Translation:** Native Obsidian blocks and `[[block:links]]` are translated into standard Astro components during the emission phase.

## Workflow
1. **Brainstorm:** User sets an Obsidian note status to `🧠 Brainstorm`. DAIS generates an outline based on the graph.
2. **Draft:** User writes prose and adds images to the Obsidian Database.
3. **Sync:** User sets status to `✨ Process`. 
    - DAIS classifies "Lived Experience" photos.
    - DAIS links entities to ArangoDB.
    - DAIS generates GEO metadata.
    - DAIS injects metadata back into the Obsidian file.
4. **Publish:** DAIS emits the final MDX for Astro and sets status to `🚀 Published`.

## Benefits
- **Longevity:** Data is local and portable (Markdown + Graph).
- **Intelligence:** The system "learns" from every post, providing deeper context over time.
- **Privacy:** All AI inference (LM Studio) and database hosting (ArangoDB) is local.
