# CLAUDE.md

Context for future Claude Code sessions. Read this first.

## What this is

A static blog at `darbeeschasingrainbows.com` — family / faith / RV life /
homeschool / Kingdom Farm. **Astro 6**, **Tailwind CSS v4**, **DaisyUI v5**,
**Cloudflare Pages**. 

- **Frontend**: Plain HTML output, no client JS framework.
- **CMS**: Local-only Deno Fresh CMS (Preact) under `cms/` for content management.

## Commands (Astro)

| Task                     | Command                | When             |
| ------------------------ | ---------------------- | ---------------- |
| Local dev (HMR)          | `npm run dev`          | Port 4321        |
| Production build         | `npm run build`        | Output → `dist/` |
| Preview production build | `npm run preview`      | After `build`    |
| Type check               | `npm run check`        | CI runs this     |
| Lint                     | `npm run lint`         | CI runs this     |
| Format check             | `npm run format:check` | CI runs this     |
| Broken link check        | `npm run check:links`  | CI runs this     |
| Playwright tests         | `npm test`             | CI runs this     |

## Commands (CMS)

| Task                     | Command                | When             |
| ------------------------ | ---------------------- | ---------------- |
| CMS Local dev            | `deno task dev`        | Inside `cms/`    |
| CMS Type check           | `deno task check`      | Inside `cms/`    |

CI workflow: [.github/workflows/ci.yml](.github/workflows/ci.yml). 

## Directory map

```
cms/                 Deno Fresh CMS (Local-only content editor)
  islands/           Interactive Preact components (GeoOptimizer, etc.)
  routes/            CMS pages and API endpoints (api/geo.ts)
src/
  components/        23 .astro components (centralized Card/Layout model)
  content/
    blog/            general blog posts (.mdx)
    projects/        DIY/build posts with parts list (.mdx)
    field-notes/     hikes, history, nature studies (.mdx)
    books/           bookshelf reviews (with llmFields)
    _templates/      starter MDX with all frontmatter fields filled in
  layouts/           BaseLayout + shared PostLayout (slots: meta, header-extras, before-content)
  pages/             file-based routing with static Tag/Category archives
  styles/global.css  Tailwind directives + DaisyUI themes + Darbees tokens
  utils/             formatDate, slugify, readingTime, getPosts, seo (JSON-LD)
  consts.ts          SITE_*, NAV_LINKS, BRAND_*, env-var re-exports
  content.config.ts  Zod schemas for all collections (sharing llmFields)
public/              static assets (favicon, llms.txt, og images, logo.svg)
```

## Where to put new code

| What you want to add                   | Where it goes                                                                                                                                                                                           |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A blog post                            | `src/content/blog/<slug>.mdx` — copy from `_templates/blog-post-template.mdx`                                                                                                                           |
| A project (DIY/build)                  | `src/content/projects/<slug>.mdx`                                                                                                                                                                       |
| A field note                           | `src/content/field-notes/<slug>.mdx`                                                                                                                                                                    |
| Reusable Astro UI element              | `src/components/<Name>.astro`                                                                                                                                                                           |
| CMS Interactive feature                | `cms/islands/<Name>.tsx`                                                                                                                                                                                |
| CMS API Endpoint                       | `cms/routes/api/<name>.ts`                                                                                                                                                                              |
| Site-wide constant                     | `src/consts.ts`                                                                                                                                                                                         |

## Content collection schemas (summary)

All collections share an `llmFields` block for **GEO (Generative Engine Optimization)**: `aiSummary`, `keyTakeaways`, `entityMentions`, `faq`, `sources`, `imageAlt`.

- `imageAlt`: Accessible alt text for hero image. Defaults to post title.
- `books`:rating (green|yellow|parent-read|red), takes (dad, mom, kids).

Source of truth: [src/content.config.ts](src/content.config.ts). 

## Design tokens

- **Fonts**: Fraunces (Serif), Nunito (Display), Inter (Body).
- **Colors**: `primary` (Forest Green), `secondary` (Gold), `accent` (Terracotta), `base-100` (Cream).

## Routing

- Dynamic: `pages/blog/[...slug].astro`, `pages/projects/[...slug].astro`, `pages/field-notes/[...slug].astro`.
- Archives: `pages/tag/[tag].astro`, `pages/category/[category].astro`.
- Bookshelf: index + per-book detail route `pages/bookshelf/[id].astro`.

## Component conventions

- `.astro` files: tabs, single quotes, trailing commas.
- Comments: JSX fragments (`<>...</>`) **must** use `{/* */}`.
- Layouts: Use the centralized `PostLayout.astro` for all posts.
- Cards: Use the centralized `PostCard.astro` (discriminated union prop).

## Tech debt (prioritized)

1. **CMS validation** — add client-side inline error messages.
2. **Cross-collection tag archives** — current archives only index blog posts.
3. **Project Category archives** — no landing pages for project-specific categories.
4. **Zod migration** — `z.string().url()` to `z.url()` (Astro 6.2 deprecation).

## Things to be careful about

- **LM Studio**: CMS optimization requires LM Studio at `http://localhost:1234/v1`.
- **Prettier Parse Errors**: HTML comments in JSX fragments break build.
- **Tailwind v4 JIT**: Avoid commas in arbitrary values in HTML; use CSS classes.
- **Typescript 6**: No narrowing across destructuring — cast `data` or use `entry.data.field`.

## When in doubt
Read [HANDOFF.md](HANDOFF.md) for a full history of remediation phases.
