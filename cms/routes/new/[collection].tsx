import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import { ContentForm } from "../../components/ContentForm.tsx";
import {
  assertCollection,
  readTemplate,
  writeEntry,
} from "../../lib/content.ts";
import { frontmatterFromRequest } from "../../lib/form.ts";
import { collections } from "../../lib/schemas.ts";
import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../../utils.ts";

const define = createDefine<State>();

export const handler = define.handlers({
  async GET(ctx) {
    const collection = assertCollection(ctx.params.collection);
    return {
      data: { collection, template: await readTemplate(collection), error: "" },
    };
  },
  async POST(ctx) {
    const collection = assertCollection(ctx.params.collection);
    try {
      const payload = await frontmatterFromRequest(collection, ctx.req);
      await writeEntry(
        collection,
        payload.slug,
        payload.frontmatter,
        payload.body,
        false,
      );
      return Response.redirect(
        new URL(`/edit/${collection}/${payload.slug}`, ctx.url),
        303,
      );
    } catch (error) {
      const template = await readTemplate(collection);
      return {
        data: {
          collection,
          template,
          error: error instanceof Error
            ? error.message
            : "Could not create entry.",
        },
      };
    }
  },
});

export default define.page<typeof handler>(function NewEntry({ data }) {
  const definition = collections[data.collection];

  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>New {definition.label} · Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-5xl">
        <a
          class="text-sm font-medium text-stone-600 hover:text-stone-950"
          href="/"
        >
          ← Dashboard
        </a>
        <header class="my-6">
          <p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">
            Create
          </p>
          <h1 class="mt-2 text-3xl font-bold tracking-tight text-stone-950">
            New {definition.label}
          </h1>
        </header>
        <ContentForm
          collection={definition}
          frontmatter={data.template.frontmatter}
          body={data.template.body}
          mode="create"
          error={data.error}
          showSeoPreview={true}
        />
      </div>
    </main>
  );
});
