import { App, PluginSettingTab, Setting } from 'obsidian';
import type DarbeeMemoryPlugin from './main';
import type { MemoryKind, Scope, Settings } from './types';

export const DEFAULT_SETTINGS: Settings = {
  bridgeUrl: 'http://localhost:5000',
  defaultTenant: 'private',
  defaultKind: 'observation',
  debounceMs: 2000,
  defaultScope: 'both',
};

const KINDS: ReadonlyArray<MemoryKind> = ['observation', 'fact', 'decision'];
const SCOPES: ReadonlyArray<Scope> = ['posts', 'private', 'both'];

export class DarbeeMemorySettingTab extends PluginSettingTab {
  plugin: DarbeeMemoryPlugin;

  constructor(app: App, plugin: DarbeeMemoryPlugin) {
    super(app, plugin);
    this.plugin = plugin;
  }

  display(): void {
    const { containerEl } = this;
    containerEl.empty();

    new Setting(containerEl)
      .setName('Bridge URL')
      .setDesc('Base URL of the DAIS bridge (localhost expected).')
      .addText((text) =>
        text
          .setValue(this.plugin.settings.bridgeUrl)
          .onChange(async (value) => {
            try {
              new URL(value); // validation
              this.plugin.settings.bridgeUrl = value;
              await this.plugin.saveSettings();
            } catch {
              /* keep previous value on invalid URL */
            }
          }),
      );

    new Setting(containerEl)
      .setName('Default tenant')
      .setDesc('Tenant id applied when a note has no `memory_tenant` frontmatter.')
      .addText((text) =>
        text
          .setValue(this.plugin.settings.defaultTenant)
          .onChange(async (value) => {
            this.plugin.settings.defaultTenant = value || 'private';
            await this.plugin.saveSettings();
          }),
      );

    new Setting(containerEl)
      .setName('Default kind')
      .setDesc('Used when a note has no `memory_kind` frontmatter.')
      .addDropdown((dd) => {
        for (const k of KINDS) dd.addOption(k, k);
        dd.setValue(this.plugin.settings.defaultKind).onChange(async (value) => {
          this.plugin.settings.defaultKind = value as MemoryKind;
          await this.plugin.saveSettings();
        });
      });

    new Setting(containerEl)
      .setName('Debounce (ms)')
      .setDesc('How long to wait after a save before flushing the batch.')
      .addText((text) =>
        text
          .setValue(String(this.plugin.settings.debounceMs))
          .onChange(async (value) => {
            const n = Number(value);
            if (Number.isFinite(n) && n >= 0) {
              this.plugin.settings.debounceMs = n;
              await this.plugin.saveSettings();
            }
          }),
      );

    new Setting(containerEl)
      .setName('Sidebar default scope')
      .addDropdown((dd) => {
        for (const s of SCOPES) dd.addOption(s, s);
        dd.setValue(this.plugin.settings.defaultScope).onChange(async (value) => {
          this.plugin.settings.defaultScope = value as Scope;
          await this.plugin.saveSettings();
        });
      });
  }
}
