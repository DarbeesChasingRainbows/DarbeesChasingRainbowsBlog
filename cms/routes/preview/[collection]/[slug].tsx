import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import {
  assertCollection,
  assertSlug,
  readEntry,
} from "../../../lib/content.ts";
import { collections } from "../../../lib/schemas.ts";
import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../../../utils.ts";

const define = createDefine<State>();

export const handler = define.handlers({
  async GET(ctx) {
    const collection = assertCollection(ctx.params.collection);
    const slug = assertSlug(ctx.params.slug);
    return {
      data: {
        collection,
        slug,
        document: await readEntry(collection, slug),
      },
    };
  },
});

export default define.page<typeof handler>(function PreviewEntry({ data }) {
  const definition = collections[data.collection];
  const { frontmatter, body } = data.document;

  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>Preview {data.slug} · Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-5xl">
        <div class="mb-6 flex items-center justify-between">
          <a
            class="text-sm font-medium text-stone-600 hover:text-stone-950"
            href={`/edit/${data.collection}/${data.slug}`}
          >
            ← Back to Edit
          </a>
          <div class="flex items-center gap-4">
            <span class="rounded-full bg-amber-100 px-3 py-1 text-sm font-medium text-amber-800">
              Draft Preview
            </span>
          </div>
        </div>
        <header class="mb-8 border-b border-stone-200 pb-6">
          <p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">
            Preview {definition.label}
          </p>
          <h1 class="mt-2 text-3xl font-bold tracking-tight text-stone-950">
            {String(frontmatter.title ?? data.slug)}
          </h1>
          {frontmatter.description && (
            <p class="mt-3 text-lg text-stone-600">
              {String(frontmatter.description)}
            </p>
          )}
        </header>
        <article class="prose prose-stone max-w-none">
          <pre class="whitespace-pre-wrap font-sans">{body}</pre>
        </article>
      </div>
    </main>
  );
});
