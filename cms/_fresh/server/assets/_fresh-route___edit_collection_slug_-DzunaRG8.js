import { a, u, s, H as Head, c as createDefine } from "../server-entry.js";
import { C as ContentForm, f as frontmatterFromRequest } from "./form-CfvaR1BG.js";
import { c as collections, a as assertCollection, b as assertSlug, w as writeEntry, r as readEntry } from "./content-BXNbgoEc.js";
const $$_tpl_1 = ['<main class="min-h-screen bg-stone-50 px-6 py-8">', '<div class="mx-auto max-w-5xl">', '<header class="my-6"><p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">Edit ', '</p><h1 class="mt-2 text-3xl font-bold tracking-tight text-stone-950">', ".mdx</h1></header>", "</div></main>"];
const define = createDefine();
const handler$1 = define.handlers({
  async GET(ctx) {
    const collection = assertCollection(ctx.params.collection);
    const slug = assertSlug(ctx.params.slug);
    return {
      data: {
        collection,
        slug,
        document: await readEntry(collection, slug),
        error: ""
      }
    };
  },
  async POST(ctx) {
    const collection = assertCollection(ctx.params.collection);
    const slug = assertSlug(ctx.params.slug);
    try {
      const payload = await frontmatterFromRequest(collection, ctx.req);
      await writeEntry(collection, slug, payload.frontmatter, payload.body, true);
      return Response.redirect(new URL(`/edit/${collection}/${slug}`, ctx.url), 303);
    } catch (error) {
      return {
        data: {
          collection,
          slug,
          document: await readEntry(collection, slug),
          error: error instanceof Error ? error.message : "Could not save entry."
        }
      };
    }
  }
});
const _slug_ = define.page(function EditEntry({
  data
}) {
  const definition = collections[data.collection];
  return a($$_tpl_1, u(Head, {
    children: u("title", {
      children: ["Edit ", data.slug, " · Darbees Local CMS"]
    })
  }), u("a", {
    class: "text-sm font-medium text-stone-600 hover:text-stone-950",
    href: "/",
    children: "← Dashboard"
  }), s(definition.label), s(data.slug), u(ContentForm, {
    collection: definition,
    slug: data.slug,
    frontmatter: data.document.frontmatter,
    body: data.document.body,
    mode: "edit",
    error: data.error
  }));
});
const routeCss = null;
const css = routeCss;
const config = void 0;
const handler = handler$1;
const handlers = void 0;
const _freshRoute___edit_collection_slug_ = _slug_;
export {
  config,
  css,
  _freshRoute___edit_collection_slug_ as default,
  handler,
  handlers
};
