export type MemoryKind = 'observation' | 'fact' | 'decision';
export type SearchKind = 'post' | MemoryKind;
export type Scope = 'posts' | 'private' | 'both';

export interface Settings {
  bridgeUrl: string;
  defaultTenant: string;
  defaultKind: MemoryKind;
  debounceMs: number;
  defaultScope: Scope;
}

export interface NoteRecord {
  key: string;
  kind: MemoryKind;
  text: string;
  title: string;
  metadata?: Record<string, unknown>;
}

export interface IngestPayload {
  tenant: string;
  notes: NoteRecord[];
  currentKeys: string[];
}

export interface SearchHit {
  slug: string;
  collection: string;
  title: string;
  matchedKind: string;
  score: number;
  snippet: string;
  url: string;
  kind: string;
  tenant: string;
}

export interface SearchResponse {
  queryEmbedMs: number;
  searchMs: number;
  results: SearchHit[];
}

export interface QueuedNote {
  key: string;
  title: string;
  kind: MemoryKind;
  body: string;
}
