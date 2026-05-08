import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import { type ContentEntry, listDrafts } from "../lib/content.ts";
import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../utils.ts";

const define = createDefine<State>();

export const handler = define.handlers({
  async GET() {
    const drafts = await listDrafts();
    return { data: { drafts } };
  },
});

export default define.page<typeof handler>(function DraftsDashboard({ data }) {
  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>Drafts · Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-7xl">
        <div class="mb-8 flex items-center justify-between">
          <div>
            <h1 class="text-3xl font-bold tracking-tight text-stone-950">
              Drafts
            </h1>
            <p class="mt-2 text-stone-600">
              {data.drafts.length} draft{data.drafts.length !== 1 ? "s" : ""}
            </p>
          </div>
          <a
            class="rounded-md bg-emerald-700 px-5 py-2.5 text-sm font-semibold text-white hover:bg-emerald-800"
            href="/"
          >
            Back to Dashboard
          </a>
        </div>

        {data.drafts.length === 0
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
                  d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
                />
              </svg>
              <p class="mt-4 text-lg font-medium text-stone-900">
                No drafts yet
              </p>
              <p class="mt-2 text-stone-600">
                Create your first draft to get started.
              </p>
            </div>
          )
          : (
            <div class="overflow-hidden rounded-lg bg-white shadow-sm">
              <table class="min-w-full divide-y divide-stone-200">
                <thead class="bg-stone-50">
                  <tr>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Title
                    </th>
                    <th class="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-stone-500">
                      Collection
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
                  {data.drafts.map((draft: ContentEntry) => (
                    <tr class="hover:bg-stone-50">
                      <td class="whitespace-nowrap px-6 py-4">
                        <div class="text-sm font-medium text-stone-900">
                          {draft.title}
                        </div>
                        <div class="text-sm text-stone-500">
                          {draft.description}
                        </div>
                      </td>
                      <td class="whitespace-nowrap px-6 py-4">
                        <span class="inline-flex rounded-full bg-amber-100 px-2 py-1 text-xs font-semibold text-amber-800">
                          {draft.collection}
                        </span>
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-sm text-stone-500">
                        {draft.pubDate}
                      </td>
                      <td class="whitespace-nowrap px-6 py-4 text-right text-sm">
                        <a
                          href={`/edit/${draft.collection}/${draft.slug}`}
                          class="font-medium text-emerald-600 hover:text-emerald-900 mr-4"
                        >
                          Edit
                        </a>
                        <a
                          href={`/preview/${draft.collection}/${draft.slug}`}
                          target="_blank"
                          rel="noopener"
                          class="font-medium text-stone-600 hover:text-stone-900"
                        >
                          Preview
                        </a>
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
