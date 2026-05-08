import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import {
  type CollectionKey,
  type ContentVersion,
  getEntryHistory,
  readEntry,
  restoreEntryVersion,
} from "../../../lib/content.ts";
import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../../../utils.ts";

const define = createDefine<State>();

export const handler = define.handlers({
  async GET(ctx) {
    const collection = ctx.params.collection as CollectionKey;
    const slug = ctx.params.slug as string;
    const history = await getEntryHistory(collection, slug);
    const current = await readEntry(collection, slug);
    return { data: { collection, slug, history, current } };
  },
  async POST(ctx) {
    const form = await ctx.req.formData();
    const hash = form.get("hash") as string;
    const collection = ctx.params.collection as CollectionKey;
    const slug = ctx.params.slug as string;

    await restoreEntryVersion(collection, slug, hash);

    return Response.redirect(`/edit/${collection}/${slug}`);
  },
});

export default define.page<typeof handler>(function VersionHistory({ data }) {
  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>Version History · {data.slug} · Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-5xl">
        <div class="mb-8 flex items-center justify-between">
          <div>
            <h1 class="text-3xl font-bold tracking-tight text-stone-950">
              Version History
            </h1>
            <p class="mt-2 text-stone-600">
              {data.collection} / {data.slug}
            </p>
          </div>
          <a
            class="rounded-md border border-stone-300 bg-white px-5 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-50"
            href={`/edit/${data.collection}/${data.slug}`}
          >
            Back to Edit
          </a>
        </div>

        {data.history.length === 0
          ? (
            <div class="rounded-lg border-2 border-dashed border-stone-300 bg-white p-12 text-center">
              <svg
                class="mx-auto h-12 w-12 text-stone-400"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width={2}
                  d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
                />
              </svg>
              <p class="mt-4 text-lg font-medium text-stone-900">
                No version history
              </p>
              <p class="mt-2 text-stone-600">
                Git history is not available for this file.
              </p>
            </div>
          )
          : (
            <div class="overflow-hidden rounded-lg bg-white shadow-sm">
              <table class="min-w-full divide-y divide-stone-200">
                <thead class="bg-stone-50">
                  <tr>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Commit
                    </th>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Message
                    </th>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Author
                    </th>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Date
                    </th>
                    <th class="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-stone-500">
                      Actions
                    </th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-stone-200">
                  {data.history.map((
                    version: ContentVersion,
                    index: number,
                  ) => (
                    <tr class="hover:bg-stone-50">
                      <td class="whitespace-nowrap px-6 py-4">
                        <code class="text-xs font-mono text-stone-600">
                          {version.hash.slice(0, 8)}
                        </code>
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-sm text-stone-900">
                        {version.message || "No message"}
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-sm text-stone-600">
                        {version.author}
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-sm text-stone-500">
                        {new Date(version.date).toLocaleString()}
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-right text-sm">
                        {index !== 0 && (
                          <form method="post">
                            <input
                              type="hidden"
                              name="hash"
                              value={version.hash}
                            />
                            <button
                              type="submit"
                              class="font-medium text-emerald-600 hover:text-emerald-900"
                            >
                              Restore
                            </button>
                          </form>
                        )}
                        {index === 0 && (
                          <span class="text-stone-400">Current</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </div>
    </main>
  );
});
