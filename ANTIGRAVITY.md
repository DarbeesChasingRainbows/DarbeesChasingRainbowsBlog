# ANTIGRAVITY.md

Project Intelligence & Context for the Antigravity IDE.

## Core Directives

1.  **Generative Engine Optimization (GEO) First**: All content must be optimized for RAG retrieval and LLM ingestion. Use the `llmFields` (aiSummary, keyTakeaways, entityMentions, faq) rigorously.
2.  **Zero-Click Value**: Assume users will read AI summaries. Provide high-density "chunks" that establish authority and "pre-sell" the click.
3.  **Local AI Privacy**: The CMS uses **LM Studio** (`http://localhost:1234/v1`) for metadata generation. Never leak draft content to public cloud APIs unless explicitly configured.
4.  **Astro 6 + Tailwind 4**: Stick to the modern static-first architecture. No client JS on the public site.

## Directory Intelligence

- `/src/content/`: The "Entity-Based" data store. Zod-validated MDX.
- `/cms/`: The "Command Center". Built with Deno Fresh for local execution.
- `/src/layouts/PostLayout.astro`: The master template. Uses slots for `meta`, `header-extras`, and `before-content`.

## Technical Constraints

- **Type Safety**: TypeScript 6.0+ strict mode. 
- **Styling**: Tailwind CSS v4 + DaisyUI v5. Use semantic themes (`forest`, `rainbow`).
- **Performance**: 100% static output. Zero broken links (verified by custom post-build script).

## Knowledge Graph (GEO Fields)

| Field | Purpose |
|-------|---------|
| `aiSummary` | Dense, factual 2-3 sentence chunk for RAG citation. |
| `keyTakeaways` | Bulleted list of citable facts. |
| `entityMentions` | Core nouns/concepts to map in the AI's latent space. |
| `faq` | Conversational Q&A pairs matching user search patterns. |

## Workflow

1.  **Draft**: Create MDX in `src/content/`.
2.  **Optimize**: Use the CMS "Generate GEO Data" button (LM Studio required).
3.  **Validate**: `npm run check` and `npm run check:links`.
4.  **Deploy**: Cloudflare Pages (GitHub triggered).

## IDE Support (Context7)

Use `ctx7` to fetch up-to-date documentation for:
- Astro 6.x
- Tailwind CSS v4
- Deno Fresh (for CMS work)
- Zod (for schema validation)

---
*Optimized for Generative Intelligence.*
