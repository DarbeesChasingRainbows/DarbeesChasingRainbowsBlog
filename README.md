# Darbees Chasing Rainbows

> Family, faith, RV life, homeschool field notes, useful projects, and the road toward Kingdom Farm.

A fast, mobile-first static blog built with **Astro 6**, **Tailwind CSS v4**, and **DaisyUI v5**. Designed for **Cloudflare Pages**.

---

## Tech stack

- **Framework**: [Astro](https://astro.build/) (static output, near-zero JavaScript)
- **Content**: Markdown + MDX via Astro Content Collections
- **Styling**: Tailwind CSS v4 + DaisyUI v5 (`forest` and `rainbow` themes)
- **Fonts** (self-hosted via `astro:assets`):
  - **Fraunces** — editorial serif used for the masthead and post titles (`.masthead` / `.font-editorial`)
  - **Nunito** — display sans for nav, eyebrows, and UI labels
  - **Inter** — body copy
- **Hosting**: Cloudflare Pages (free, global edge, unlimited bandwidth)
- **Analytics**: Cloudflare Web Analytics (token via `PUBLIC_CLOUDFLARE_ANALYTICS_TOKEN` env var — see `.env.example`)
- **Newsletter**: Buttondown (handle via `PUBLIC_BUTTONDOWN_HANDLE` env var)

## Project structure

```
src/
  components/        # Reusable UI components
  content/
    blog/            # General blog posts (.md / .mdx)
    projects/        # DIY/project posts with parts lists
    field-notes/     # Hikes, history, nature studies
  layouts/           # BaseLayout + per-collection layouts
  pages/             # File-based routing
  styles/global.css  # Tailwind + DaisyUI themes + Darbees tokens
  utils/             # Date, slugify, reading time, getPosts, SEO
  consts.ts          # Site title, nav links, social links, analytics token
  content.config.ts  # Zod schemas for all 3 collections
public/
  images/            # blog/, projects/, field-notes/, brand/
astro.config.mjs
package.json
```

## Local setup

```bash
npm install
npm run dev          # http://localhost:4321
npm run build        # Produces /dist
npm run preview      # Preview the production build
```

Requires **Node 22.12+** (matches Astro 6 engine requirement).

## Adding content

### Add a blog post

Create `src/content/blog/your-slug.mdx`:

```mdx
---
title: 'Your title'
description: '150–160 char SEO description'
pubDate: 2026-04-29
category: 'RV Life'
tags: ['rv', 'family']
draft: false
---

Your content here. You can `import Callout from '../../components/Callout.astro';` for rich blocks.
```

### Add a project post

Create `src/content/projects/your-slug.mdx`:

```mdx
---
title: 'Project name'
description: 'Short description'
pubDate: 2026-04-29
category: 'RV Automation'
tags: ['arduino', 'diy']
difficulty: 'medium' # easy | medium | hard
estimatedCost: '$45–60'
estimatedTime: '3–4 hours'
githubUrl: 'https://github.com/...'
partsList:
  - name: 'Arduino Nano'
    quantity: 1
    url: 'https://...'
    notes: 'Optional'
---
```

The `partsList` is rendered automatically as a table at the top of the post.

### Add a field note

Create `src/content/field-notes/your-slug.mdx`:

```mdx
---
title: 'Place name'
description: 'Short summary'
pubDate: 2026-04-29
location: 'Bulow Plantation Ruins'
region: 'Florida'
weather: 'Sunny, 78°F'
category: 'History'
tags: ['florida', 'homeschool']
includesHomeschool: true
---

import FieldNotesBlock from '../../components/FieldNotesBlock.astro';

<FieldNotesBlock
  location="..."
  saw={['one', 'two', 'three']}
  heard="..."
  wondered="..."
  learned="..."
/>
```

## Image guidelines

- Place images in `public/images/{blog,projects,field-notes,brand}/`
- Reference them in frontmatter as `featuredImage: ../../assets/your-image.jpg` (or use `/images/...` URL paths)
- Keep hero images under 2 MB; 1200–2000 px wide is plenty
- **Always** include meaningful `alt` text

## Reusable components for MDX

Imported from `../../components/`:

- `<Callout type="note|tip|warning|faith|field-note|project" title="...">`
- `<ProjectPartsList items={[...]} />` (auto-used from frontmatter on project pages)
- `<FieldNotesBlock location="..." saw={[...]} heard="..." wondered="..." learned="..." />`

## Themes

Two DaisyUI themes are defined in `src/styles/global.css`:

- **`forest`** (default) — forest green / warm gold / off-white
- **`rainbow`** — navy / soft yellow / pastel rainbow

Switch the active theme by changing `data-theme="forest"` to `data-theme="rainbow"` on the `<html>` tag in `src/layouts/BaseLayout.astro`.

## SEO, RSS, sitemap

- **SEO**: `src/components/BaseHead.astro` handles `<title>`, meta description, Open Graph, Twitter cards, and canonical URLs. Article pages also emit JSON-LD structured data.
- **RSS**: `/rss.xml` aggregates all three collections.
- **Sitemap**: Generated automatically by `@astrojs/sitemap` at `/sitemap-index.xml`.

## Deployment — Cloudflare Pages

1. Push this repo to GitHub.
2. In Cloudflare dashboard → **Pages** → **Create a project** → connect the GitHub repo.
3. **Build settings**:
   - Framework preset: **Astro**
   - Build command: `npm run build`
   - Build output directory: `dist`
   - Root directory: (blank)
   - Environment variable: `NODE_VERSION` = `22.12.0` (or higher)
4. Click **Save and Deploy**.
5. Add your custom domain (Cloudflare DNS makes this trivial).
6. Enable **Always Use HTTPS** and **Automatic HTTPS Rewrites**.

Preview deployments are automatic on every PR.

### Environment variables

Copy `.env.example` to `.env` for local development. In production, set the
same keys in Cloudflare Pages → **Settings → Environment variables**.

| Key                                 | What it does                                | Behavior when unset                           |
| ----------------------------------- | ------------------------------------------- | --------------------------------------------- |
| `PUBLIC_CLOUDFLARE_ANALYTICS_TOKEN` | Enables the Cloudflare Web Analytics beacon | Beacon script not emitted                     |
| `PUBLIC_BUTTONDOWN_HANDLE`          | Newsletter form action                      | `NewsletterSignup` falls back (see component) |

All keys are `PUBLIC_*` — they're inlined into the client bundle at build time.
Anything truly secret should be a server-only variable (no `PUBLIC_` prefix)
and should never appear in `.env.example`.

## Roadmap

Designed-for but not yet built. **No promised timeline** — these are
intentional non-goals for the initial launch:

- Cloudflare Images integration
- [Giscus](https://giscus.app/) comments on post pages
- Tag and category archive pages
- Per-book detail route (`/bookshelf/[id]`)
- Search (Pagefind)
- Photo galleries
- Map of field notes
- Members-only resources
- CI/CD: typecheck + build on PR; preview deploys
- Lint/format: ESLint + Prettier with Astro presets
- Tests: Playwright smoke tests on preview deploys

## License

All site content (writing, photos) is © The Darbees, all rights reserved.
The site code is yours to read and learn from — please don't lift the brand.
