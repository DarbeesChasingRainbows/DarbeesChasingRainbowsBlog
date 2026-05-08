# Darbees CMS — Directus + SQLite

Local-only content management for the Darbees Chasing Rainbows blog.  
Directus provides structured editors, GEO optimization (via LM Studio), and MDX export to the Astro content directory.

## Quick Start

```bash
# From the project root:
cd cms

# Start Directus (first run creates the database automatically)
docker run -d --name darbees-cms -p 8055:8055 ^
  -v %cd%\database:/directus/database ^
  -v %cd%\uploads:/directus/uploads ^
  -v %cd%\extensions:/directus/extensions ^
  -v %cd%\..\src\content:/directus/astro-content ^
  -e SECRET=replace-with-random-value ^
  -e ADMIN_EMAIL=admin@darbees.com ^
  -e ADMIN_PASSWORD=localdev ^
  -e CONTENT_EXPORT_PATH=/directus/astro-content ^
  directus/directus

# Open admin: http://localhost:8055
# Login: admin@darbees.com / localdev

# Set up collections (one-time):
npm install
npx tsx scripts/setup-schema.ts

# Migrate existing content into Directus (one-time):
npx tsx scripts/migrate-content.ts
```

## Architecture

```
Author writes in Directus (localhost:8055)
    ↓
GEO hook populates AI/SEO fields (via LM Studio)
    ↓
Export hook writes .mdx to src/content/
    ↓
git add . && git commit && git push
    ↓
GitHub → Cloudflare Pages: astro build
    ↓
54+ static pages deployed — Directus never involved
```

## Directory Structure

```
cms/
├── database/           # SQLite DB (gitignored, copy to migrate machines)
├── uploads/            # Directus file storage (gitignored)
├── extensions/
│   ├── hooks/
│   │   ├── geo-optimizer/   # LM Studio → GEO fields
│   │   └── mdx-export/     # Directus → MDX files
│   └── endpoints/
│       └── export/          # Manual export endpoint
├── scripts/
│   ├── setup-schema.ts      # Creates Directus collections (one-time)
│   └── migrate-content.ts   # Imports existing MDX into Directus (one-time)
├── .env.example
├── .gitignore
└── README.md
```

## Portability (Framework Desktop)

```bash
# Just copy the cms/ directory — SQLite is one file
# On the new machine:
docker run -d --name darbees-cms -p 8055:8055 ...  # same command as above
# Done — all content, images, and config travel with the folder
```

## Extensions

### GEO Optimizer Hook
- Fires when `geo_status` is set to `pending`
- Calls LM Studio to generate `aiSummary`, `keyTakeaways`, `entityMentions`, `faq`
- Sets `geo_status` to `generated` on success

### MDX Export Hook
- Fires when `status` changes to `published`
- Assembles `body_blocks` into MDX with component imports
- Writes `.mdx` to `src/content/{collection}/`

### Manual Export Endpoint
- `GET http://localhost:8055/custom/export/all` — export all published content
- `GET http://localhost:8055/custom/export/blog_posts` — export one collection
- `GET http://localhost:8055/custom/export/blog_posts/my-slug` — export single item
