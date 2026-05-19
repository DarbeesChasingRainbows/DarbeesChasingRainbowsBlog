import { ItemView, WorkspaceLeaf } from 'obsidian';
import type DarbeeMemoryPlugin from './main';
import { scopeToFilters } from './ingester';
import { searchMemory } from './bridge-client';
import type { Scope, SearchHit } from './types';

export const VIEW_TYPE_SIDEBAR = 'darbee-memory-sidebar';

export class DarbeeMemorySidebar extends ItemView {
  plugin: DarbeeMemoryPlugin;
  currentScope: Scope;
  controller: AbortController | null = null;
  queryInput!: HTMLInputElement;
  statusEl!: HTMLElement;
  resultsEl!: HTMLElement;

  constructor(leaf: WorkspaceLeaf, plugin: DarbeeMemoryPlugin) {
    super(leaf);
    this.plugin = plugin;
    this.currentScope = plugin.settings.defaultScope;
  }

  getViewType() {
    return VIEW_TYPE_SIDEBAR;
  }

  getDisplayText() {
    return 'Darbee Memory';
  }

  async onOpen() {
    const container = this.containerEl.children[1] as HTMLElement;
    container.empty();
    container.createEl('h3', { text: 'Darbee Memory' });

    const form = container.createEl('form');
    form.style.display = 'flex';
    form.style.flexDirection = 'column';
    form.style.gap = '6px';
    form.style.marginBottom = '8px';

    this.queryInput = form.createEl('input', {
      type: 'text',
      placeholder: 'Search posts and notes…',
    }) as HTMLInputElement;

    const toggleEl = form.createEl('div');
    toggleEl.style.display = 'flex';
    toggleEl.style.gap = '4px';
    const scopes: Scope[] = ['posts', 'private', 'both'];
    const buttons: Record<Scope, HTMLButtonElement> = {} as Record<Scope, HTMLButtonElement>;
    for (const s of scopes) {
      const btn = toggleEl.createEl('button', { text: s, type: 'button' }) as HTMLButtonElement;
      btn.onclick = () => {
        this.currentScope = s;
        for (const other of scopes) buttons[other].removeClass('mod-cta');
        btn.addClass('mod-cta');
      };
      buttons[s] = btn;
    }
    buttons[this.currentScope].addClass('mod-cta');

    const submit = form.createEl('button', { text: 'Search', type: 'submit' });
    this.statusEl = container.createEl('p', { text: '' });
    this.statusEl.style.minHeight = '1.4em';
    this.statusEl.setAttr('role', 'status');
    this.resultsEl = container.createEl('div');

    form.onsubmit = async (e) => {
      e.preventDefault();
      submit.setAttr('disabled', 'true');
      try {
        await this.runQuery();
      } finally {
        submit.removeAttribute('disabled');
      }
    };
  }

  async onClose() {
    this.controller?.abort();
  }

  private async runQuery() {
    const query = this.queryInput.value.trim();
    if (!query) return;

    this.controller?.abort();
    this.controller = new AbortController();
    const { kinds, tenants } = scopeToFilters(this.currentScope);

    this.statusEl.setText('Searching…');
    this.resultsEl.empty();

    try {
      const resp = await searchMemory(this.plugin.settings.bridgeUrl, {
        query,
        k: 10,
        kinds,
        tenants,
      });
      if (resp.results.length === 0) {
        this.statusEl.setText('No results.');
        return;
      }
      this.statusEl.setText(
        `${resp.results.length} result${resp.results.length === 1 ? '' : 's'} (embed ${resp.queryEmbedMs}ms · search ${resp.searchMs}ms)`,
      );
      this.renderResults(resp.results);
    } catch (err) {
      this.statusEl.setText(`Error: ${(err as Error).message}`);
    }
  }

  private renderResults(results: SearchHit[]) {
    for (const r of results) {
      const card = this.resultsEl.createEl('div');
      card.style.border = '1px solid var(--background-modifier-border)';
      card.style.borderRadius = '4px';
      card.style.padding = '6px';
      card.style.marginBottom = '6px';
      card.style.cursor = 'pointer';

      const header = card.createEl('div');
      header.style.display = 'flex';
      header.style.justifyContent = 'space-between';
      header.style.gap = '6px';

      header.createEl('strong', { text: r.title || r.slug });
      const badge = header.createEl('span', { text: `${r.score.toFixed(3)} · ${r.kind}/${r.tenant}` });
      badge.style.fontSize = '11px';
      badge.style.opacity = '0.7';

      const snippet = card.createEl('p', { text: r.snippet });
      snippet.style.fontSize = '12px';
      snippet.style.margin = '4px 0';
      snippet.style.opacity = '0.85';

      card.onclick = () => {
        if (r.kind === 'post') {
          window.open(`${this.plugin.settings.bridgeUrl.replace(/:\d+$/, '')}${r.url}`, '_blank');
        } else {
          this.app.workspace.openLinkText(r.slug.replace(/^obsidian:\/\//, ''), '');
        }
      };
    }
  }
}
