import { a, u, s, H as Head, c as createDefine } from "../server-entry.js";
import { C as ContentForm, f as frontmatterFromRequest } from "./form-CfvaR1BG.js";
import { c as collections, a as assertCollection, w as writeEntry, d as readTemplate } from "./content-BXNbgoEc.js";
const $$_tpl_1 = ['<main class="min-h-screen bg-stone-50 px-6 py-8">', '<div class="mx-auto max-w-5xl">', '<header class="my-6"><p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">Create</p><h1 class="mt-2 text-3xl font-bold tracking-tight text-stone-950">New ', "</h1></header>", "</div></main>"];
const define = createDefine();
const handler$1 = define.handlers({
  async GET(ctx) {
    const collection = assertCollection(ctx.params.collection);
    return {
      data: {
        collection,
        template: await readTemplate(collection),
        error: ""
      }
    };
  },
  async POST(ctx) {
    const collection = assertCollection(ctx.params.collection);
    try {
      const payload = await frontmatterFromRequest(collection, ctx.req);
      await writeEntry(collection, payload.slug, payload.frontmatter, payload.body, false);
      return Response.redirect(new URL(`/edit/${collection}/${payload.slug}`, ctx.url), 303);
    } catch (error) {
      const template = await readTemplate(collection);
      return {
        data: {
          collection,
          template,
          error: error instanceof Error ? error.message : "Could not create entry."
        }
      };
    }
  }
});
const _collection_ = define.page(function NewEntry({
  data
}) {
  const definition = collections[data.collection];
  return a($$_tpl_1, u(Head, {
    children: u("title", {
      children: ["New ", definition.label, " · Darbees Local CMS"]
    })
  }), u("a", {
    class: "text-sm font-medium text-stone-600 hover:text-stone-950",
    href: "/",
    children: "← Dashboard"
  }), s(definition.label), u(ContentForm, {
    collection: definition,
    frontmatter: data.template.frontmatter,
    body: data.template.body,
    mode: "create",
    error: data.error
  }));
});
const routeCss = null;
const css = routeCss;
const config = void 0;
const handler = handler$1;
const handlers = void 0;
const _freshRoute___new_collection_ = _collection_;
export {
  config,
  css,
  _freshRoute___new_collection_ as default,
  handler,
  handlers
};
