# Darbee Memory (Obsidian plugin)

Live-sync Obsidian notes flagged `memory: true` into the Darbee memory bridge,
and search across published posts + private notes from the sidebar.

## Install (one-time)

```bash
cd obsidian-plugin
npm install
npm run build
cd ..
npm run obsidian:link
```

Then enable "Darbee Memory" in **Obsidian → Community Plugins**.

## Develop

```bash
npm run obsidian:dev   # watch-mode esbuild; pair with Obsidian "Hot Reload" plugin
```

## Frontmatter contract

```yaml
---
memory: true
memory_kind: observation       # observation | fact | decision (default: observation)
memory_tenant: private         # default: from plugin settings
---
```

## Uninstall

Disable in Obsidian, optionally `npm run obsidian:unlink`. Memory rows linger
in Arango until the plugin's next save sends an empty `currentKeys`.
