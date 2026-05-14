# Authoring scripts

Author-run helpers for the Obsidian → `.mdx` → Astro pipeline. All three call
the local LM Studio instance and never run at build or CI time. Each leaves a
diff you review before committing.

Requires these env vars (in `.env`, see `.env.example`): `LMSTUDIO_URL`,
`LMSTUDIO_API_KEY`, `AI_MODEL_ID`, `AI_VISION_MODEL_ID`, `AI_EMBEDDING_MODEL_ID`.

| Script | Command | What it does |
| ------ | ------- | ------------ |
| `geo-fill.mjs` | `npm run geo:fill -- <post.mdx>` / `npm run geo:fill:all` | Fill `aiSummary` / `keyTakeaways` / `faq`. `-- --force` overwrites. |
| `related-rebuild.mjs` | `npm run related:rebuild` | Embed every published post, write `src/data/related-posts.json`. |
| `image-watcher.mjs` | `npm run image:watch` | Watch `obsidian-templates/inbox/<collection>/<slug>/`; convert + place + alt-text dropped photos. |

Unit tests: `npm run test:scripts`.

## Manual verification checklist

Requires LM Studio running with chat, embedding, and vision models loaded.

1. `npm run geo:fill -- src/content/blog/<draft>.mdx` on a post with empty GEO
   fields → fields populated, diff printed, body unchanged.
2. Re-run → reports "already populated"; add `-- --force` → regenerates.
3. `npm run related:rebuild` → `src/data/related-posts.json` written; every
   entry has `score >= 0.6` and `<= 3` per post; second run reports all-from-cache.
4. `npm run build` → `PostFooterNav` related section renders for a post with
   relations, renders nothing for an orphan post.
5. `npm run image:watch`, drop a `.heic` into
   `obsidian-templates/inbox/blog/<slug>/` → JPEG appears under
   `src/assets/blog/<slug>/`, alt text + Markdown snippet printed, inbox source
   deleted. Drop into a bad folder name → file left in place, error logged.
