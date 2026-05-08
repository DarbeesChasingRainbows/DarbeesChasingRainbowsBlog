import type { CollectionDefinition, FieldDefinition } from "../lib/schemas.ts";
import type { Frontmatter } from "../lib/frontmatter.ts";

interface MetadataDrawerProps {
  collection: CollectionDefinition;
  frontmatter: Frontmatter;
  isOpen: boolean;
  onClose: () => void;
}

export function MetadataDrawer(
  { collection, frontmatter, isOpen, onClose }: MetadataDrawerProps,
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

  const llmFields = collection.fields.filter((field) =>
    llmFieldNames.includes(field.name)
  );

  return (
    <>
      {isOpen && (
        <div
          class="fixed inset-0 z-50 bg-black/50 transition-opacity"
          onClick={onClose}
        />
      )}
      <div
        class={`fixed right-0 top-0 z-50 h-full w-full max-w-lg bg-white shadow-xl transition-transform duration-300 ease-in-out ${
          isOpen ? "translate-x-0" : "translate-x-full"
        }`}
      >
        <div class="flex h-full flex-col">
          <div class="flex items-center justify-between border-b border-stone-200 px-6 py-4">
            <h2 class="text-lg font-semibold text-stone-900">
              Advanced Metadata
            </h2>
            <button
              type="button"
              onClick={onClose}
              class="rounded-md p-2 text-stone-400 hover:bg-stone-100 hover:text-stone-600"
            >
              <svg
                class="h-5 w-5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width={2}
                  d="M6 18L18 6M6 6l12 12"
                />
              </svg>
            </button>
          </div>
          <div class="flex-1 overflow-y-auto px-6 py-6">
            <div class="space-y-4">
              <p class="text-sm text-stone-600">
                These fields are optional but recommended for SEO and AI
                discoverability.
              </p>
              {llmFields.map((field) => (
                <FieldInput
                  key={field.name}
                  field={field}
                  value={frontmatter[field.name]}
                  frontmatter={frontmatter}
                />
              ))}
            </div>
          </div>
          <div class="border-t border-stone-200 px-6 py-4">
            <button
              type="button"
              onClick={onClose}
              class="w-full rounded-md bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-800"
            >
              Done
            </button>
          </div>
        </div>
      </div>
    </>
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
    : String(displayValue ?? "");

  if (field.kind === "textarea" || field.kind === "list") {
    return (
      <label class="block">
        <span class="text-sm font-medium text-stone-700">
          {field.label}
        </span>
        <textarea
          class={`${common} min-h-24`}
          name={field.name}
        >
          {stringValue}
        </textarea>
        {field.help && (
          <span class="mt-1 block text-xs text-stone-500">{field.help}</span>
        )}
      </label>
    );
  }

  return (
    <label class="block">
      <span class="text-sm font-medium text-stone-700">
        {field.label}
      </span>
      <input
        class={common}
        type="text"
        name={field.name}
        value={stringValue}
      />
      {field.help && (
        <span class="mt-1 block text-xs text-stone-500">{field.help}</span>
      )}
    </label>
  );
}
