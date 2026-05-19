import { requestUrl } from 'obsidian';
import type { IngestPayload, SearchResponse } from './types';

const DEFAULT_TIMEOUT_MS = 30_000;

export class BridgeError extends Error {
  status?: number;
  body?: unknown;

  constructor(message: string, opts: { status?: number; body?: unknown } = {}) {
    super(message);
    this.name = 'BridgeError';
    this.status = opts.status;
    this.body = opts.body;
  }
}

export interface IngestNotesResponse {
  embeddedCount: number;
  cachedCount: number;
  failedCount: number;
  staleDeletedCount: number;
  durationMs: number;
  perNote: Array<{ key: string; outcome: string; reason?: string }>;
}

async function postJson<T>(url: string, body: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<T> {
  let response;
  try {
    response = await Promise.race([
      requestUrl({
        url,
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body),
        throw: false,
      }),
      new Promise<never>((_, reject) =>
        setTimeout(() => reject(new BridgeError(`bridge timeout after ${timeoutMs}ms: ${url}`)), timeoutMs),
      ),
    ]);
  } catch (err) {
    if (err instanceof BridgeError) throw err;
    throw new BridgeError(`bridge unreachable at ${url}: ${(err as Error).message}`);
  }

  if (response.status < 200 || response.status >= 300) {
    throw new BridgeError(`bridge ${response.status}`, {
      status: response.status,
      body: (response as { json?: unknown }).json ?? response.text,
    });
  }

  return ((response as { json?: unknown }).json ?? JSON.parse(response.text)) as T;
}

export async function ingestNotes(
  baseUrl: string,
  payload: IngestPayload,
  timeoutMs?: number,
): Promise<IngestNotesResponse> {
  return postJson<IngestNotesResponse>(
    `${baseUrl.replace(/\/$/, '')}/api/memory/ingest-notes`,
    payload,
    timeoutMs,
  );
}

export interface SearchPayload {
  query: string;
  k: number;
  kinds: string[];
  tenants: string[];
}

export async function searchMemory(
  baseUrl: string,
  payload: SearchPayload,
  timeoutMs?: number,
): Promise<SearchResponse> {
  return postJson<SearchResponse>(
    `${baseUrl.replace(/\/$/, '')}/api/memory/search`,
    payload,
    timeoutMs,
  );
}
