# GEMINI.md

Core mandates and project-specific instructions for Gemini CLI.

## Foundation

This is a static-first blog platform built with **Astro 6** and **Tailwind 4**, featuring a local **Deno Fresh CMS** for content management. 

**Core Goal**: Maximize **Generative Engine Optimization (GEO)** to ensure content is citable and authoritative in the "Zero-Click Reality" of AI-driven search.

## Tech Stack & Commands

- **Site**: Astro 6.2+, TypeScript 6.0+, Tailwind 4.0+, DaisyUI 5.0+.
- **CMS**: Deno Fresh 2.3+, Preact, LM Studio (local OpenAI-compatible API).
- **Quality**: Playwright smoke tests, custom internal-link checker, Lighthouse CI.

| Tool | Port/URL |
|------|----------|
| Astro Dev | `http://localhost:4321` |
| CMS Dev | `http://localhost:5173` (via `deno task dev`) |
| LM Studio | `http://localhost:1234/v1` |

## Engineering Standards

1.  **GEO Fields Mandatory**: Every content entry must leverage `llmFields` (`aiSummary`, `keyTakeaways`, `entityMentions`, `faq`).
2.  **Centralized Components**: Use `PostLayout.astro` and `PostCard.astro`. Do not create specific cards/layouts for new collections unless the data model is fundamentally incompatible (e.g., Books).
3.  **Local Privacy**: Prefer local processing (LM Studio) for metadata generation.
4.  **No HTML Comments**: Never use `<!-- -->` inside JSX/Astro fragments. Use `{/* */}`.

## Directory Strategy

- `cms/routes/api/geo.ts`: The bridge to LM Studio for automated GEO data.
- `cms/islands/GeoOptimizer.tsx`: Client-side trigger for GEO generation.
- `src/utils/seo.ts`: Central location for JSON-LD generation.
- `src/consts.ts`: Single source of truth for site constants and brand tokens.

## Workflow: Drafting & Optimization

When adding or editing content:
1.  Verify the MDX body text exists.
2.  Use the `GeoOptimizer` logic (or trigger via CMS) to populate RAG-optimized metadata.
3.  Ensure `imageAlt` is descriptive of the image contents for accessibility and AI vision retrieval.
4.  Run `npm run check:links` after any routing or slug change.

---
*Stay factual. Stay dense. Optimize for retrieval.*
