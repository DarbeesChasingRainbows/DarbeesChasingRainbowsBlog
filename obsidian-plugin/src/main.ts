import { Plugin, TFile, WorkspaceLeaf } from 'obsidian';
import { DEFAULT_SETTINGS, DarbeeMemorySettingTab } from './settings';
import { DarbeeMemorySidebar, VIEW_TYPE_SIDEBAR } from './sidebar-view';
import { ingestNotes } from './bridge-client';
import { parseNoteFrontmatter, deriveNoteKey, bodyFromRaw, buildIngestPayload } from './ingester';
import type { Settings, QueuedNote } from './types';

// Vault subset that the runtime needs; matches Obsidian's Vault for the fields used.
export interface VaultLike {
  getMarkdownFiles(): Array<{ path: string }>;
  read(file: { path: string }): Promise<string>;
  on(event: 'modify', cb: (file: { path: string }) => void): unknown;
}

export class DarbeeMemoryRuntime {
  settings: Settings;
  vault: VaultLike;
  queue: Map<string, QueuedNote> = new Map();
  timer: ReturnType<typeof setTimeout> | null = null;
  inFlight: Promise<unknown> | null = null;
  pendingFlushChain: Promise<unknown> = Promise.resolve();

  constructor(settings: Settings, vault: VaultLike) {
    this.settings = settings;
    this.vault = vault;
  }

  handleModify(file: { path: string }): void {
    this.pendingFlushChain = this.pendingFlushChain.then(async () => {
      const raw = await this.vault.read(file);
      const parsed = parseNoteFrontmatter(raw, this.settings);
      if (!parsed.shouldIngest) return;
      const body = bodyFromRaw(raw);
      if (body.trim().length === 0) {
        console.warn(`[darbee-memory] dropping empty-body note: ${file.path}`);
        return;
      }
      const key = deriveNoteKey(file.path);
      const title = file.path.replace(/\.md$/, '').split('/').pop() ?? file.path;
      this.queue.set(key, { key, title, kind: parsed.kind, body });
      this.scheduleFlush();
    });
  }

  private scheduleFlush(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.timer = null;
      this.pendingFlushChain = this.pendingFlushChain.then(() => this.flush());
    }, this.settings.debounceMs);
  }

  private async flush(): Promise<void> {
    if (this.inFlight) {
      await this.inFlight;
    }
    if (this.queue.size === 0) return;

    const drained = Array.from(this.queue.values());
    this.queue.clear();

    // currentKeys: every note in the vault that still has memory:true.
    const all = this.vault.getMarkdownFiles();
    const currentKeys: string[] = [];
    for (const f of all) {
      const raw = await this.vault.read(f);
      const parsed = parseNoteFrontmatter(raw, this.settings);
      if (parsed.shouldIngest) currentKeys.push(deriveNoteKey(f.path));
    }

    const payload = buildIngestPayload({
      tenant: this.settings.defaultTenant,
      queued: drained,
      currentKeys,
    });
    if (payload.notes.length === 0 && currentKeys.length === 0) return;

    this.inFlight = ingestNotes(this.settings.bridgeUrl, payload);
    try {
      await this.inFlight;
    } finally {
      this.inFlight = null;
    }
  }
}

// Test helper: awaits any pending flush chain so tests can assert post-debounce state.
export async function runDebounceFlush(runtime: DarbeeMemoryRuntime): Promise<void> {
  await runtime.pendingFlushChain;
}

export default class DarbeeMemoryPlugin extends Plugin {
  settings: Settings = DEFAULT_SETTINGS;
  runtime!: DarbeeMemoryRuntime;

  async onload() {
    await this.loadSettings();
    this.runtime = new DarbeeMemoryRuntime(this.settings, this.app.vault as unknown as VaultLike);

    this.registerEvent(
      this.app.vault.on('modify', (file) => {
        if (file instanceof TFile && file.extension === 'md') {
          this.runtime.handleModify({ path: file.path });
        }
      }),
    );

    this.registerView(VIEW_TYPE_SIDEBAR, (leaf: WorkspaceLeaf) => new DarbeeMemorySidebar(leaf, this));

    this.addCommand({
      id: 'open-sidebar',
      name: 'Open sidebar',
      callback: () => this.activateSidebar(),
    });

    this.addCommand({
      id: 'ingest-now',
      name: 'Ingest flagged notes now',
      callback: async () => {
        const files = this.app.vault.getMarkdownFiles();
        for (const f of files) this.runtime.handleModify({ path: f.path });
      },
    });

    this.addSettingTab(new DarbeeMemorySettingTab(this.app, this));
  }

  async loadSettings() {
    this.settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
  }

  async saveSettings() {
    await this.saveData(this.settings);
  }

  async activateSidebar() {
    const { workspace } = this.app;
    let leaf = workspace.getLeavesOfType(VIEW_TYPE_SIDEBAR)[0];
    if (!leaf) {
      leaf = workspace.getRightLeaf(false) ?? workspace.getLeaf(true);
      await leaf.setViewState({ type: VIEW_TYPE_SIDEBAR });
    }
    workspace.revealLeaf(leaf);
  }
}
