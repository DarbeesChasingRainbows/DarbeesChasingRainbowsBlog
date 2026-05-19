import type { MemoryKind, Scope, Settings, IngestPayload, QueuedNote } from './types';

const KIND_VALUES: ReadonlyArray<MemoryKind> = ['observation', 'fact', 'decision'];

export interface ParseResult {
  shouldIngest: boolean;
  kind: MemoryKind;
  tenant: string;
}

export function parseNoteFrontmatter(raw: string, settings: Settings): ParseResult {
  const match = raw.match(/^---\n([\s\S]*?)\n---/);
  if (!match) return { shouldIngest: false, kind: settings.defaultKind, tenant: settings.defaultTenant };
  const frontmatter = match[1];
  const flag = /^memory:\s*true\s*$/m.test(frontmatter);
  if (!flag) return { shouldIngest: false, kind: settings.defaultKind, tenant: settings.defaultTenant };

  const kindMatch = frontmatter.match(/^memory_kind:\s*(\S+)\s*$/m);
  let kind = settings.defaultKind;
  if (kindMatch) {
    const candidate = kindMatch[1].toLowerCase() as MemoryKind;
    if (KIND_VALUES.includes(candidate)) {
      kind = candidate;
    } else {
      console.warn(`[darbee-memory] unknown memory_kind="${kindMatch[1]}" — falling back to ${settings.defaultKind}`);
    }
  }

  const tenantMatch = frontmatter.match(/^memory_tenant:\s*(\S+)\s*$/m);
  const tenant = tenantMatch ? tenantMatch[1] : settings.defaultTenant;

  return { shouldIngest: true, kind, tenant };
}

export function deriveNoteKey(vaultRelativePath: string): string {
  return `obsidian://${vaultRelativePath}`;
}

export function stripMdx(body: string): string {
  return body
    .replace(/^import\s.+$/gm, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/<[^>]+>/g, '')
    .replace(/[#*_`>|~-]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function bodyFromRaw(raw: string): string {
  const match = raw.match(/^---\n[\s\S]*?\n---\n?/);
  const body = match ? raw.slice(match[0].length) : raw;
  return stripMdx(body);
}

export interface BuildPayloadInput {
  tenant: string;
  queued: QueuedNote[];
  currentKeys: string[];
}

export function buildIngestPayload(input: BuildPayloadInput): IngestPayload {
  const notes = input.queued
    .filter((n) => n.body.trim().length > 0)
    .map((n) => ({
      key: n.key,
      kind: n.kind,
      text: n.body,
      title: n.title,
      metadata: { source: 'obsidian' },
    }));
  return { tenant: input.tenant, notes, currentKeys: input.currentKeys };
}

export interface ScopeFilters {
  kinds: string[];
  tenants: string[];
}

export function scopeToFilters(scope: Scope): ScopeFilters {
  switch (scope) {
    case 'posts':
      return { kinds: ['post'], tenants: ['public'] };
    case 'private':
      return { kinds: ['observation', 'fact', 'decision'], tenants: ['private'] };
    case 'both':
      return { kinds: ['post', 'observation', 'fact', 'decision'], tenants: ['public', 'private'] };
  }
}
