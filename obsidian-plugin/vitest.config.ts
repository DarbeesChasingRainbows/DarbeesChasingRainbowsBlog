import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  resolve: {
    alias: {
      // The 'obsidian' npm package ships types only — alias to a stub for tests.
      // Test files use vi.mock('obsidian', ...) to provide actual behavior.
      obsidian: fileURLToPath(new URL('./test/obsidian-stub-empty.ts', import.meta.url)),
    },
  },
});
