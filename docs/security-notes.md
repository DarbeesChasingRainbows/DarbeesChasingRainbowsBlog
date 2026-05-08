# Security notes

Tracker for security advisories that aren't actionable today and shouldn't
silently rot. Each entry: what the warning is, why we're not fixing it now,
what would need to change for us to revisit.

---

## `yaml@2.0.0–2.8.2` stack-overflow advisory (GHSA-48c2-rrv3-qjmp)

**First noticed:** 2026-05-03 (during Astro 6.1 → 6.2 + tooling additions).
**Status:** Not fixing. Re-evaluate quarterly or when `@astrojs/check` releases.
**`npm audit` count:** 5 moderate (all from this single dep).

### Why the warning exists

The `yaml` parser, when given pathologically nested YAML collections, can
overflow the call stack. Severity is **moderate**, not critical.

### How it reaches us

```
@astrojs/check
  └── @astrojs/language-server
      └── volar-service-yaml
          └── yaml-language-server
              └── yaml@2.x   ← the vulnerable package
```

It is a **dev-only, transitive** dependency. It's not in our deployed bundle
and it's never executed by visitors to the site.

### Why we're not fixing it

`npm audit fix --force` would downgrade `@astrojs/check` to **0.9.2** — a
**breaking** change to our type-checker that we'd have to rewrite scripts and
CI around. The cure costs more than the disease for our exposure profile (a
static blog with no user-supplied YAML in any code path).

### When we'd revisit

- `@astrojs/check` ships a release that pulls in `yaml@>=2.8.3`
  ([release feed](https://github.com/withastro/language-tools/releases)).
- We start consuming user-supplied YAML in any code path (currently we don't).
- The advisory severity is upgraded to **high** or **critical**.

### Suppression / acknowledgement

We do not silence `npm audit` in CI. The 5-count moderate finding is visible
on every install and that's intentional — when the upstream fix lands we want
the count to drop to zero on the next install.

---

## How to add a new entry

When `npm audit` flags something new and we decide not to fix it immediately:

1. Add a section here with the date, status, the dep chain, and the
   re-evaluation trigger.
2. Don't add it to a config-based suppression list — visibility is the point.
3. Re-check at most quarterly. Out-of-band re-checks are fine when an upstream
   ships a notable release.
