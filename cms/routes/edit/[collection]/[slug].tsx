import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import { ContentForm } from "../../../components/ContentForm.tsx";
import {
  assertCollection,
  assertSlug,
  readEntry,
  writeEntry,
} from "../../../lib/content.ts";
import { frontmatterFromRequest } from "../../../lib/form.ts";
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
        error: "",
      },
    };
  },
  async POST(ctx) {
    const collection = assertCollection(ctx.params.collection);
    const slug = assertSlug(ctx.params.slug);
    try {
      const payload = await frontmatterFromRequest(collection, ctx.req);
      await writeEntry(
        collection,
        slug,
        payload.frontmatter,
        payload.body,
        true,
      );
      return Response.redirect(
        new URL(`/edit/${collection}/${slug}`, ctx.url),
        303,
      );
    } catch (error) {
      return {
        data: {
          collection,
          slug,
          document: await readEntry(collection, slug),
          error: error instanceof Error
            ? error.message
            : "Could not save entry.",
        },
      };
    }
  },
});

export default define.page<typeof handler>(function EditEntry({ data }) {
  const definition = collections[data.collection];

  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>Edit {data.slug} · Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-5xl">
        <div class="mb-6 flex items-center justify-between">
          <a
            class="text-sm font-medium text-stone-600 hover:text-stone-950"
            href="/"
          >
            ← Dashboard
          </a>
          <a
            class="text-sm font-medium text-stone-600 hover:text-stone-950"
            href={`/history/${data.collection}/${data.slug}`}
          >
            Version History
          </a>
        </div>
        <header class="my-6">
          <p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">
            Edit {definition.label}
          </p>
          <h1 class="mt-2 text-3xl font-bold tracking-tight text-stone-950">
            {data.slug}.mdx
          </h1>
        </header>
        <ContentForm
          collection={definition}
          slug={data.slug}
          frontmatter={data.document.frontmatter}
          body={data.document.body}
          mode="edit"
          error={data.error}
          showSeoPreview={true}
        />
      </div>
    </main>
  );
});
