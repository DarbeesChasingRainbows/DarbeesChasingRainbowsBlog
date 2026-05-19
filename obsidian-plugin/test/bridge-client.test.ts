import { describe, it, expect, vi, afterEach } from 'vitest';
import { ingestNotes, searchMemory, BridgeError } from '../src/bridge-client';

const obsidianMock = vi.hoisted(() => ({ requestUrl: vi.fn() }));
vi.mock('obsidian', () => obsidianMock);

afterEach(() => {
  obsidianMock.requestUrl.mockReset();
});

describe('ingestNotes', () => {
  it('returns parsed response on 2xx', async () => {
    obsidianMock.requestUrl.mockResolvedValue({
      status: 200,
      json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 12, perNote: [] },
      text: '',
    });
    const out = await ingestNotes('http://localhost:5000', {
      tenant: 'private',
      notes: [],
      currentKeys: [],
    });
    expect(out.embeddedCount).toBe(1);
  });

  it('throws BridgeError with parsed body on non-2xx', async () => {
    obsidianMock.requestUrl.mockResolvedValue({
      status: 400,
      json: { error: 'invalid_request', message: 'bad' },
      text: '',
    });
    await expect(
      ingestNotes('http://localhost:5000', { tenant: 'private', notes: [], currentKeys: [] }),
    ).rejects.toBeInstanceOf(BridgeError);
  });
});

describe('searchMemory', () => {
  it('passes kinds and tenants in the request body', async () => {
    obsidianMock.requestUrl.mockResolvedValue({
      status: 200,
      json: { queryEmbedMs: 50, searchMs: 10, results: [] },
      text: '',
    });
    await searchMemory('http://localhost:5000', {
      query: 'hello',
      k: 5,
      kinds: ['post', 'observation'],
      tenants: ['public', 'private'],
    });
    const call = obsidianMock.requestUrl.mock.calls[0][0];
    const body = JSON.parse(call.body);
    expect(body.kinds).toEqual(['post', 'observation']);
    expect(body.tenants).toEqual(['public', 'private']);
  });
});
