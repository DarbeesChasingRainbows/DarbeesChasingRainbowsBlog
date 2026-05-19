import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { makeVault } from './obsidian-stub';
import { runDebounceFlush, DarbeeMemoryRuntime } from '../src/main';

const obsidianMock = vi.hoisted(() => ({
  Plugin: class {},
  ItemView: class {},
  PluginSettingTab: class {},
  Setting: class {
    setName() { return this; }
    setDesc() { return this; }
    addText() { return this; }
    addDropdown() { return this; }
  },
  Notice: vi.fn(),
  TFile: class { path = ''; extension = ''; },
  WorkspaceLeaf: class {},
  requestUrl: vi.fn(),
}));
vi.mock('obsidian', () => obsidianMock);

beforeEach(() => {
  vi.useFakeTimers();
  obsidianMock.requestUrl.mockResolvedValue({
    status: 200,
    json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
    text: '',
  });
});

afterEach(() => {
  vi.useRealTimers();
  obsidianMock.requestUrl.mockReset();
});

const baseSettings = {
  bridgeUrl: 'http://localhost:5000',
  defaultTenant: 'private',
  defaultKind: 'observation' as const,
  debounceMs: 2000,
  defaultScope: 'both' as const,
};

describe('runtime debounce', () => {
  it('coalesces 3 saves within window into one ingest call', async () => {
    const vault = makeVault([
      { path: 'a.md', content: '---\nmemory: true\n---\nhello a' },
      { path: 'b.md', content: '---\nmemory: true\n---\nhello b' },
      { path: 'c.md', content: '---\nmemory: true\n---\nhello c' },
    ]);
    const runtime = new DarbeeMemoryRuntime(baseSettings, vault as any);

    runtime.handleModify({ path: 'a.md' });
    runtime.handleModify({ path: 'b.md' });
    runtime.handleModify({ path: 'c.md' });

    await vi.advanceTimersByTimeAsync(2100);
    await runDebounceFlush(runtime);
    expect(obsidianMock.requestUrl).toHaveBeenCalledTimes(1);
    const body = JSON.parse(obsidianMock.requestUrl.mock.calls[0][0].body);
    expect(body.notes.length).toBe(3);
  });

  it('does not call bridge when only un-flagged notes are saved', async () => {
    const vault = makeVault([{ path: 'plain.md', content: 'no frontmatter' }]);
    const runtime = new DarbeeMemoryRuntime(baseSettings, vault as any);
    runtime.handleModify({ path: 'plain.md' });
    await vi.advanceTimersByTimeAsync(2100);
    await runDebounceFlush(runtime);
    expect(obsidianMock.requestUrl).not.toHaveBeenCalled();
  });

  it('drops notes with empty bodies before sending', async () => {
    const vault = makeVault([{ path: 'empty.md', content: '---\nmemory: true\n---\n\n' }]);
    const runtime = new DarbeeMemoryRuntime(baseSettings, vault as any);
    runtime.handleModify({ path: 'empty.md' });
    await vi.advanceTimersByTimeAsync(2100);
    await runDebounceFlush(runtime);
    expect(obsidianMock.requestUrl).not.toHaveBeenCalled();
  });

  it('buffers a save while a previous ingest is in flight', async () => {
    const vault = makeVault([
      { path: 'a.md', content: '---\nmemory: true\n---\nhello a' },
      { path: 'b.md', content: '---\nmemory: true\n---\nhello b' },
    ]);
    let resolveFirst: (v: unknown) => void = () => {};
    obsidianMock.requestUrl.mockImplementationOnce(
      () => new Promise((r) => { resolveFirst = r; }),
    );
    obsidianMock.requestUrl.mockResolvedValueOnce({
      status: 200,
      json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
      text: '',
    });

    const runtime = new DarbeeMemoryRuntime(baseSettings, vault as any);

    runtime.handleModify({ path: 'a.md' });
    await vi.advanceTimersByTimeAsync(2100);
    // First flush is now in-flight (mock pending). Queue another save; it's
    // chained behind the in-flight flush via pendingFlushChain.
    runtime.handleModify({ path: 'b.md' });
    // Resolve the first call so the chain can advance to b's handleModify.
    resolveFirst({
      status: 200,
      json: { embeddedCount: 1, cachedCount: 0, failedCount: 0, staleDeletedCount: 0, durationMs: 1, perNote: [] },
      text: '',
    });
    // Now advance again to fire b's debounce timer.
    await vi.advanceTimersByTimeAsync(2100);
    await runDebounceFlush(runtime);

    expect(obsidianMock.requestUrl).toHaveBeenCalledTimes(2);
  });
});
