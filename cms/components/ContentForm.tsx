import type { CollectionDefinition, FieldDefinition } from "../lib/schemas.ts";
import type { Frontmatter } from "../lib/frontmatter.ts";
import { CONTENT_TAGS } from "../lib/schemas.ts";
import GeoOptimizer from "../islands/GeoOptimizer.tsx";
import SeoPreview from "../islands/SeoPreview.tsx";

interface ContentFormProps {
  collection: CollectionDefinition;
  slug?: string;
  frontmatter: Frontmatter;
  body: string;
  mode: "create" | "edit";
  error?: string;
  showSeoPreview?: boolean;
}

export function ContentForm(
  { collection, slug, frontmatter, body, mode, error, showSeoPreview = false }:
    ContentFormProps,
) {
  const llmFieldNames = [
    "aiSummary",
    "keyTakeaways",
    "entityMentions",
    "faq",
    "sources",
    "imageAlt",
    "imageAttributionName",
    "imageAttributionUrl",
    "preview",
  ];

  const mainFields = collection.fields.filter((field) =>
    !llmFieldNames.includes(field.name)
  );

  const llmFields = collection.fields.filter((field) =>
    llmFieldNames.includes(field.name)
  );

  return (
    <form method="post" enctype="multipart/form-data" class="space-y-8">
      {error && (
        <div class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">
          {error}
        </div>
      )}

      <section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
        <h2 class="text-lg font-semibold text-stone-900">File</h2>
        <div class="mt-4 grid gap-4 md:grid-cols-2">
          <label class="block">
            <span class="text-sm font-medium text-stone-700">Collection</span>
            <input
              class="mt-1 w-full rounded-md border border-stone-300 bg-stone-100 px-3 py-2"
              value={collection.label}
              disabled
            />
          </label>
          <label class="block">
            <span class="text-sm font-medium text-stone-700">Slug</span>
            <input
              class="mt-1 w-full rounded-md border border-stone-300 px-3 py-2"
              name="slug"
              required
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
              value={slug ?? ""}
              readonly={mode === "edit"}
              placeholder="my-kebab-case-title"
            />
          </label>
        </div>
      </section>

      <section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
        <h2 class="text-lg font-semibold text-stone-900">Frontmatter</h2>
        <div class="mt-4 grid gap-4 md:grid-cols-2">
          {mainFields.map((field) => (
            <FieldInput
              key={field.name}
              field={field}
              value={frontmatter[field.name]}
              frontmatter={frontmatter}
            />
          ))}
        </div>
      </section>

      <details class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
        <summary class="cursor-pointer text-lg font-semibold text-stone-900 hover:text-stone-700">
          Advanced Metadata (AI/SEO fields)
        </summary>
        <div class="mt-4 grid gap-4 md:grid-cols-2">
          <GeoOptimizer />
          {llmFields.map((field) => (
            <FieldInput
              key={field.name}
              field={field}
              value={frontmatter[field.name]}
              frontmatter={frontmatter}
            />
          ))}
        </div>
      </details>

      <section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm">
        <label class="block">
          <span class="text-lg font-semibold text-stone-900">MDX body</span>
          <textarea
            class="mt-4 min-h-112 w-full rounded-md border border-stone-300 px-3 py-2 font-mono text-sm"
            name="body"
          >
            {body}
          </textarea>
        </label>
      </section>

      {showSeoPreview && (
        <SeoPreview
          initialTitle={String(frontmatter.title ?? "")}
          initialDescription={String(frontmatter.description ?? "")}
        />
      )}

      <div class="sticky bottom-0 flex items-center justify-between border-t border-stone-200 bg-stone-50/95 px-4 py-4 backdrop-blur">
        <a
          class="text-sm font-medium text-stone-600 hover:text-stone-900"
          href="/"
        >
          Cancel
        </a>
        <div class="flex gap-3">
          {mode === "edit" && (
            <a
              class="rounded-md border border-stone-300 bg-white px-5 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-50"
              href={`/preview/${collection.key}/${slug}`}
              target="_blank"
              rel="noopener"
            >
              Preview
            </a>
          )}
          <button
            class="rounded-md bg-emerald-700 px-5 py-2.5 text-sm font-semibold text-white hover:bg-emerald-800"
            type="submit"
          >
            {mode === "create" ? "Create MDX file" : "Save MDX file"}
          </button>
        </div>
      </div>
    </form>
  );
}

function FieldInput(
  { field, value, frontmatter }: {
    field: FieldDefinition;
    value: unknown;
    frontmatter: Frontmatter;
  },
) {
  const common = "mt-1 w-full rounded-md border border-stone-300 px-3 py-2";

  // Handle imageAttributionName - extract from imageAttribution object if present
  let displayValue = value;
  if (field.name === "imageAttributionName" && value === undefined) {
    const imageAttribution = frontmatter[field.name.replace("Name", "")] as {
      name?: string;
      url?: string;
    } | undefined;
    displayValue = imageAttribution?.name ?? "";
  }
  if (field.name === "imageAttributionUrl" && value === undefined) {
    const imageAttribution = frontmatter[field.name.replace("Url", "")] as {
      name?: string;
      url?: string;
    } | undefined;
    displayValue = imageAttribution?.url ?? "";
  }

  const stringValue = Array.isArray(displayValue)
    ? displayValue.join("\n")
    : String(displayValue ?? defaultValue(field));

  if (field.kind === "checkbox") {
    return (
      <label class="flex items-center gap-3 rounded-md border border-stone-200 p-3">
        <input
          type="checkbox"
          name={field.name}
          value="true"
          checked={value === true ||
            value === undefined && field.name === "draft"}
        />
        <span class="text-sm font-medium text-stone-700">{field.label}</span>
      </label>
    );
  }

  if (field.kind === "tags") {
    const tags = Array.isArray(value) ? value : [];
    const tagsString = tags.join(", ");
    return (
      <label class="block md:col-span-2">
        <span class="text-sm font-medium text-stone-700">
          {field.label}
          {field.required ? " *" : ""}
        </span>
        <input
          class={`${common} w-full`}
          type="text"
          name={field.name}
          value={tagsString}
          placeholder="Comma-separated tags"
          list={`${field.name}_suggestions`}
        />
        <datalist id={`${field.name}_suggestions`}>
          {CONTENT_TAGS.map((tag) => <option key={tag} value={tag} />)}
        </datalist>
        {field.help && (
          <span class="mt-1 block text-xs text-stone-500">{field.help}</span>
        )}
      </label>
    );
  }

  if (field.kind === "textarea" || field.kind === "list") {
    return (
      <label class="block md:col-span-2">
        <span class="text-sm font-medium text-stone-700">
          {field.label}
          {field.required ? " *" : ""}
        </span>
        <textarea
          class={`${common} min-h-24`}
          name={field.name}
          required={field.required}
        >
          {stringValue}
        </textarea>
        {field.help && (
          <span class="mt-1 block text-xs text-stone-500">{field.help}</span>
        )}
      </label>
    );
  }

  if (field.kind === "image") {
    return (
      <label class="block md:col-span-2">
        <span class="text-sm font-medium text-stone-700">
          {field.label}
          {field.required ? " *" : ""}
        </span>
        <div class="mt-1 space-y-2">
          <div class="relative rounded-md border-2 border-dashed border-stone-300 p-6 transition-colors hover:border-stone-400">
            <input
              class="absolute inset-0 cursor-pointer opacity-0"
              type="file"
              name={`${field.name}_file`}
              accept="image/*"
              onDragOver={(e) => {
                e.preventDefault();
                e.currentTarget.classList.add(
                  "border-emerald-500",
                  "bg-emerald-50",
                );
              }}
              onDragLeave={(e) => {
                e.preventDefault();
                e.currentTarget.classList.remove(
                  "border-emerald-500",
                  "bg-emerald-50",
                );
              }}
              onDrop={(e) => {
                e.preventDefault();
                e.currentTarget.classList.remove(
                  "border-emerald-500",
                  "bg-emerald-50",
                );
              }}
              onChange={(e) => {
                const file = (e.target as HTMLInputElement).files?.[0];
                if (file) {
                  const reader = new FileReader();
                  reader.onload = (event) => {
                    const preview = e.currentTarget.parentElement
                      ?.querySelector(
                        ".image-preview",
                      ) as HTMLImageElement;
                    if (preview && event.target?.result) {
                      preview.src = event.target.result as string;
                      preview.classList.remove("hidden");
                    }
                  };
                  reader.readAsDataURL(file);
                }
              }}
            />
            <div class="text-center">
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
                  d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                />
              </svg>
              <p class="mt-2 text-sm text-stone-600">
                <span class="font-medium text-emerald-600">
                  Click to upload
                </span>{" "}
                or drag and drop
              </p>
              <p class="text-xs text-stone-500">PNG, JPG, GIF up to 10MB</p>
            </div>
            {stringValue && (
              <img
                src={stringValue as string}
                alt="Preview"
                class="image-preview mt-4 mx-auto max-h-48 rounded-md object-cover hidden"
              />
            )}
          </div>
          <div class="flex items-center gap-2">
            <span class="text-sm text-stone-500">Or enter path:</span>
            <input
              class={`${common} flex-1`}
              type="text"
              name={field.name}
              value={stringValue}
              placeholder="e.g., ./images/photo.jpg"
            />
          </div>
          {field.help && (
            <span class="block text-xs text-stone-500">{field.help}</span>
          )}
        </div>
      </label>
    );
  }

  if (field.kind === "select") {
    return (
      <label class="block">
        <span class="text-sm font-medium text-stone-700">
          {field.label}
          {field.required ? " *" : ""}
        </span>
        <select class={common} name={field.name} required={field.required}>
          {(field.options ?? []).map((option) => (
            <option
              key={option}
              value={option}
              selected={option === stringValue}
            >
              {option || "None"}
            </option>
          ))}
        </select>
      </label>
    );
  }

  return (
    <label class="block">
      <span class="text-sm font-medium text-stone-700">
        {field.label}
        {field.required ? " *" : ""}
      </span>
      <input
        class={common}
        type={field.kind === "date" ? "date" : "text"}
        name={field.name}
        value={stringValue}
        required={field.required}
      />
      {field.help && (
        <span class="mt-1 block text-xs text-stone-500">{field.help}</span>
      )}
    </label>
  );
}

function defaultValue(field: FieldDefinition): string | boolean {
  if (field.name === "draft") return true;
  if (field.kind === "date") return new Date().toISOString().slice(0, 10);
  return "";
}
