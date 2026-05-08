import { a, s, u, H as Head, c as createDefine } from "../server-entry.js";
import { c as collections, l as listEntries } from "./content-BXNbgoEc.js";
const $$_tpl_1 = ['<main class="min-h-screen bg-stone-50 px-6 py-8">', '<div class="mx-auto max-w-6xl"><header class="mb-8"><p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">Local-only file CMS</p><h1 class="mt-2 text-4xl font-bold tracking-tight text-stone-950">Darbees Chasing Rainbows</h1><p class="mt-3 max-w-2xl text-stone-600">Create and edit Astro MDX content files without adding a hosted CMS or database.</p></header><section class="grid gap-4 md:grid-cols-4">', '</section><section class="mt-10 rounded-xl border border-stone-200 bg-white shadow-sm"><div class="border-b border-stone-200 px-5 py-4"><h2 class="text-lg font-semibold text-stone-900">Existing entries</h2></div><div class="divide-y divide-stone-100">', "", "</div></section></div></main>"];
const $$_tpl_2 = ['<span class="text-sm font-medium text-stone-500">New</span><h2 class="mt-2 text-xl font-semibold text-stone-900">', '</h2><p class="mt-3 text-sm text-stone-600">Write to src/content/', "</p>"];
const $$_tpl_3 = ['<p class="p-5 text-sm text-stone-500">No content entries found yet.</p>'];
const $$_tpl_4 = ['<div class="flex items-center justify-between gap-4"><div><p class="text-sm font-medium text-emerald-700">', " · ", '</p><h3 class="mt-1 font-semibold text-stone-900">', '</h3><p class="mt-1 line-clamp-1 text-sm text-stone-600">', "</p></div>", "</div>"];
const $$_tpl_5 = ['<span class="rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-800">Draft</span>'];
const define = createDefine();
const handler$1 = define.handlers({
  async GET(_ctx) {
    return {
      data: {
        entries: await listEntries()
      }
    };
  }
});
const index = define.page(function Home({
  data
}) {
  return a($$_tpl_1, u(Head, {
    children: u("title", {
      children: "Darbees Local CMS"
    })
  }), s(Object.values(collections).map((collection) => u("a", {
    class: "rounded-xl border border-stone-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md",
    href: `/new/${collection.key}`,
    children: a($$_tpl_2, s(collection.label), s(collection.folder))
  }, collection.key))), s(data.entries.length === 0 && a($$_tpl_3)), s(data.entries.map((entry) => u("a", {
    class: "block px-5 py-4 hover:bg-stone-50",
    href: `/edit/${entry.collection}/${entry.slug}`,
    children: a($$_tpl_4, s(collections[entry.collection].label), s(entry.pubDate), s(entry.title), s(entry.description), s(entry.draft && a($$_tpl_5)))
  }, `${entry.collection}:${entry.slug}`))));
});
const routeCss = null;
const css = routeCss;
const config = void 0;
const handler = handler$1;
const handlers = void 0;
const _freshRoute___index = index;
export {
  config,
  css,
  _freshRoute___index as default,
  handler,
  handlers
};
