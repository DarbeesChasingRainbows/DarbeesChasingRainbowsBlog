// Empty stub for the 'obsidian' npm package, which ships TypeScript types only
// and has no runtime entry. Tests that need behaviour call `vi.mock('obsidian', ...)`.
// Provide minimal exports so plain `import { X } from 'obsidian'` resolves.
export const requestUrl = (..._args: unknown[]): unknown => {
  throw new Error("'obsidian' stub: vi.mock('obsidian', ...) was not registered");
};
export class Plugin {}
export class ItemView {}
export class PluginSettingTab {}
export class Setting {
  setName() { return this; }
  setDesc() { return this; }
  addText() { return this; }
  addDropdown() { return this; }
}
export const Notice = class { constructor(..._args: unknown[]) {} };
export class TFile {
  path = '';
  extension = '';
}
export class WorkspaceLeaf {}
