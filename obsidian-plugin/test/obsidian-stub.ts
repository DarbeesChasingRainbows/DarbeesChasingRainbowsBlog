import { vi } from 'vitest';

// A minimal in-memory Vault that the plugin's debounce logic can drive.
export interface StubFile {
  path: string;
  content: string;
}

export function makeVault(initial: StubFile[]) {
  const files = new Map<string, StubFile>(initial.map((f) => [f.path, f]));
  const listeners: Record<string, ((file: StubFile) => void)[]> = { modify: [] };
  return {
    getMarkdownFiles: vi.fn(() => Array.from(files.values()).map((f) => ({ path: f.path }))),
    read: vi.fn(async (file: { path: string }) => files.get(file.path)?.content ?? ''),
    on: vi.fn((event: string, cb: (file: StubFile) => void) => {
      (listeners[event] ??= []).push(cb);
    }),
    simulateModify(file: StubFile) {
      files.set(file.path, file);
      for (const cb of listeners.modify ?? []) cb(file);
    },
    files,
  };
}
