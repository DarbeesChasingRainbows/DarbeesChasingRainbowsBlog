import { describe, it, expect, vi } from 'vitest';
import {
  parseNoteFrontmatter,
  deriveNoteKey,
  stripMdx,
  buildIngestPayload,
  scopeToFilters,
} from '../src/ingester';

const SETTINGS = {
  bridgeUrl: 'http://localhost:5000',
  defaultTenant: 'private',
  defaultKind: 'observation' as const,
  debounceMs: 2000,
  defaultScope: 'both' as const,
};

describe('parseNoteFrontmatter', () => {
  it('returns shouldIngest=true with fact kind when frontmatter requests fact', () => {
    const res = parseNoteFrontmatter(
      '---\nmemory: true\nmemory_kind: fact\n---\nbody',
      SETTINGS,
    );
    expect(res.shouldIngest).toBe(true);
    expect(res.kind).toBe('fact');
    expect(res.tenant).toBe('private');
  });

  it('returns shouldIngest=false when memory flag is missing', () => {
    const res = parseNoteFrontmatter('---\ntitle: foo\n---\nbody', SETTINGS);
    expect(res.shouldIngest).toBe(false);
  });

  it('falls back to defaultKind on unknown memory_kind and warns', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const res = parseNoteFrontmatter(
      '---\nmemory: true\nmemory_kind: foobar\n---\nbody',
      SETTINGS,
    );
    expect(res.kind).toBe('observation');
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});

describe('deriveNoteKey', () => {
  it('produces obsidian:// prefix and preserves the path', () => {
    expect(deriveNoteKey('daily/2026-05-18.md')).toBe('obsidian://daily/2026-05-18.md');
  });
});

describe('stripMdx', () => {
  it('removes import lines, jsx, markdown punctuation, and collapses whitespace', () => {
    const input = `import Callout from '../Callout.astro';
# Heading
Some **bold** text with a [link](https://example.com).
<Callout>Hi</Callout>`;
    const out = stripMdx(input);
    expect(out).not.toContain('import');
    expect(out).not.toContain('<Callout');
    expect(out).not.toContain('**');
    expect(out).toContain('Some bold text with a link');
  });
});

describe('buildIngestPayload', () => {
  it('groups queued notes and includes currentKeys; drops empty bodies', () => {
    const payload = buildIngestPayload({
      tenant: 'private',
      queued: [
        { key: 'obsidian://a.md', title: 'A', kind: 'observation', body: 'hello' },
        { key: 'obsidian://b.md', title: 'B', kind: 'fact', body: '   ' },
      ],
      currentKeys: ['obsidian://a.md', 'obsidian://b.md'],
    });
    expect(payload.notes).toHaveLength(1);
    expect(payload.notes[0].key).toBe('obsidian://a.md');
    expect(payload.currentKeys).toContain('obsidian://b.md');
  });
});

describe('scopeToFilters', () => {
  it('maps private to observation/fact/decision kinds with private tenant', () => {
    expect(scopeToFilters('private')).toEqual({
      kinds: ['observation', 'fact', 'decision'],
      tenants: ['private'],
    });
  });

  it('maps both to 4 kinds and 2 tenants', () => {
    expect(scopeToFilters('both')).toEqual({
      kinds: ['post', 'observation', 'fact', 'decision'],
      tenants: ['public', 'private'],
    });
  });

  it('maps posts to post kind with public tenant', () => {
    expect(scopeToFilters('posts')).toEqual({
      kinds: ['post'],
      tenants: ['public'],
    });
  });
});
