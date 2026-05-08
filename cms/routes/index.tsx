import { Head } from "jsr:@fresh/core@^2.3.3/runtime";
import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../utils.ts";

const define = createDefine<State>();
import { listEntries } from "../lib/content.ts";
import { collections } from "../lib/schemas.ts";

export const handler = define.handlers({
  async GET(_ctx) {
    return { data: { entries: await listEntries() } };
  },
});

export default define.page<typeof handler>(function Home({ data }) {
  return (
    <main class="min-h-screen bg-stone-50 px-6 py-8">
      <Head>
        <title>Darbees Local CMS</title>
      </Head>
      <div class="mx-auto max-w-6xl">
        <header class="mb-8 flex items-center justify-between">
          <div>
            <p class="text-sm font-semibold uppercase tracking-wide text-emerald-700">
              Local-only file CMS
            </p>
            <h1 class="mt-2 text-4xl font-bold tracking-tight text-stone-950">
              Darbees Chasing Rainbows
            </h1>
            <p class="mt-3 max-w-2xl text-stone-600">
              Create and edit Astro MDX content files without adding a hosted
              CMS or database.
            </p>
          </div>
          <a
            class="rounded-md border border-stone-300 bg-white px-4 py-2 text-sm font-semibold text-stone-700 hover:bg-stone-50"
            href="/drafts"
          >
            View Drafts
          </a>
        </header>

        <section class="grid gap-4 md:grid-cols-4">
          {Object.values(collections).map((collection) => (
            <a
              key={collection.key}
              class="rounded-xl border border-stone-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
              href={`/new/${collection.key}`}
            >
              <span class="text-sm font-medium text-stone-500">New</span>
              <h2 class="mt-2 text-xl font-semibold text-stone-900">
                {collection.label}
              </h2>
              <p class="mt-3 text-sm text-stone-600">
                Write to src/content/{collection.folder}
              </p>
            </a>
          ))}
        </section>

        <section class="mt-10 rounded-xl border border-stone-200 bg-white shadow-sm">
          <div class="border-b border-stone-200 px-5 py-4">
            <h2 class="text-lg font-semibold text-stone-900">
              Existing entries
            </h2>
          </div>
          <div class="divide-y divide-stone-100">
            {data.entries.length === 0 && (
              <p class="p-5 text-sm text-stone-500">
                No content entries found yet.
              </p>
            )}
            {data.entries.map((entry) => (
              <a
                key={`${entry.collection}:${entry.slug}`}
                class="block px-5 py-4 hover:bg-stone-50"
                href={`/edit/${entry.collection}/${entry.slug}`}
              >
                <div class="flex items-center justify-between gap-4">
                  <div>
                    <p class="text-sm font-medium text-emerald-700">
                      {collections[entry.collection].label} · {entry.pubDate}
                    </p>
                    <h3 class="mt-1 font-semibold text-stone-900">
                      {entry.title}
                    </h3>
                    <p class="mt-1 line-clamp-1 text-sm text-stone-600">
                      {entry.description}
                    </p>
                  </div>
                  {entry.draft && (
                    <span class="rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-800">
                      Draft
                    </span>
                  )}
                </div>
              </a>
            ))}
          </div>
        </section>
      </div>
    </main>
  );
});
