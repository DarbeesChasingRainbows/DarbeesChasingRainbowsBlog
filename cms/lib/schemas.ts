export type CollectionKey = "blog" | "books" | "projects" | "field-notes";

export type FieldKind =
  | "text"
  | "textarea"
  | "date"
  | "checkbox"
  | "select"
  | "tags"
  | "list"
  | "image";

export interface FieldDefinition {
  name: string;
  label: string;
  kind: FieldKind;
  required?: boolean;
  options?: string[];
  help?: string;
}

export interface CollectionDefinition {
  key: CollectionKey;
  label: string;
  folder: string;
  templatePath: string;
  fields: FieldDefinition[];
}

export const CONTENT_CATEGORIES = [
  "RV Life",
  "Homeschool",
  "Kingdom Farm",
  "Faith & Reflections",
  "Field Notes",
  "Projects & Builds",
];

export const CONTENT_TAGS = [
  "RV",
  "Solar",
  "Water Systems",
  "DIY",
  "Automation",
  "Travel",
  "Homeschool",
  "Field Trip",
  "Nature",
  "Farm",
  "Garden",
  "Faith",
  "Family",
  "Book Review",
  "Tools",
  "Build",
  "Maintenance",
];

const llmFields: FieldDefinition[] = [
  { name: "aiSummary", label: "AI summary", kind: "textarea" },
  {
    name: "keyTakeaways",
    label: "Key takeaways",
    kind: "list",
    help: "One takeaway per line.",
  },
  {
    name: "entityMentions",
    label: "Entity mentions",
    kind: "list",
    help: "One entity per line.",
  },
  {
    name: "faq",
    label: "FAQ",
    kind: "textarea",
    help: "YAML array or leave blank.",
  },
  {
    name: "sources",
    label: "Sources",
    kind: "textarea",
    help: "YAML array or leave blank.",
  },
  { name: "imageAlt", label: "Image alt text", kind: "textarea" },
  {
    name: "imageAttributionName",
    label: "Image attribution name",
    kind: "text",
    help: "Photographer or source name",
  },
  {
    name: "imageAttributionUrl",
    label: "Image attribution URL",
    kind: "text",
    help: "Link to image source (optional)",
  },
  {
    name: "preview",
    label: "Preview/excerpt",
    kind: "textarea",
    help: "Short text for post listings",
  },
];

export const collections: Record<CollectionKey, CollectionDefinition> = {
  blog: {
    key: "blog",
    label: "Blog post",
    folder: "blog",
    templatePath: "src/content/_templates/blog-post-template.mdx",
    fields: [
      { name: "title", label: "Title", kind: "text", required: true },
      {
        name: "description",
        label: "Description",
        kind: "textarea",
        required: true,
      },
      { name: "pubDate", label: "Publish date", kind: "date", required: true },
      { name: "updatedDate", label: "Updated date", kind: "date" },
      { name: "author", label: "Author", kind: "text" },
      {
        name: "category",
        label: "Category",
        kind: "select",
        required: true,
        options: CONTENT_CATEGORIES,
      },
      {
        name: "tags",
        label: "Tags",
        kind: "tags",
        help: `Suggested: ${CONTENT_TAGS.slice(0, 5).join(", ")}...`,
      },
      {
        name: "featuredImage",
        label: "Featured image",
        kind: "image",
        help:
          "Path relative to content collection folder (e.g., ./images/photo.jpg)",
      },
      {
        name: "heroImage",
        label: "Hero image",
        kind: "image",
        help:
          "Path relative to content collection folder (e.g., ./images/hero.jpg)",
      },
      { name: "imageAlt", label: "Image alt text", kind: "textarea" },
      {
        name: "imageAttributionName",
        label: "Image attribution name",
        kind: "text",
        help: "Photographer or source name",
      },
      {
        name: "imageAttributionUrl",
        label: "Image attribution URL",
        kind: "text",
        help: "Link to image source (optional)",
      },
      {
        name: "preview",
        label: "Preview/excerpt",
        kind: "textarea",
        help: "Short text for post listings",
      },
      { name: "draft", label: "Draft", kind: "checkbox" },
      ...llmFields,
    ],
  },
  books: {
    key: "books",
    label: "Book review",
    folder: "books",
    templatePath: "src/content/_templates/book-template.mdx",
    fields: [
      { name: "title", label: "Page title", kind: "text", required: true },
      {
        name: "description",
        label: "Description",
        kind: "textarea",
        required: true,
      },
      { name: "bookTitle", label: "Book title", kind: "text", required: true },
      { name: "author", label: "Author", kind: "text", required: true },
      { name: "pubDate", label: "Publish date", kind: "date", required: true },
      {
        name: "category",
        label: "Category",
        kind: "select",
        required: true,
        options: [
          "Kids",
          "Tweens",
          "Teens",
          "Family",
          "Homeschool",
          "Kingdom Farm",
          "Theology",
          "Nature",
          "History",
        ],
      },
      { name: "ageRange", label: "Age range", kind: "text" },
      {
        name: "formatUsed",
        label: "Format used",
        kind: "select",
        options: ["", "read-aloud", "audiobook", "independent", "mixed"],
      },
      {
        name: "rating",
        label: "Rating",
        kind: "select",
        required: true,
        options: ["green", "yellow", "parent-read", "red"],
      },
      { name: "tags", label: "Tags", kind: "tags" },
      {
        name: "heroImage",
        label: "Hero image",
        kind: "image",
        help:
          "Path relative to content collection folder (e.g., ./images/hero.jpg)",
      },
      { name: "draft", label: "Draft", kind: "checkbox" },
      { name: "dadTake", label: "Dad take", kind: "textarea" },
      { name: "momTake", label: "Mom take", kind: "textarea" },
      { name: "kidsTake", label: "Kids take", kind: "textarea" },
      { name: "readAloudValue", label: "Read-aloud value", kind: "textarea" },
      { name: "audiobookValue", label: "Audiobook value", kind: "textarea" },
      {
        name: "educationalValue",
        label: "Educational value",
        kind: "textarea",
      },
      { name: "contentNotes", label: "Content notes", kind: "textarea" },
      { name: "worldviewNotes", label: "Worldview notes", kind: "textarea" },
      ...llmFields,
    ],
  },
  projects: {
    key: "projects",
    label: "Project",
    folder: "projects",
    templatePath: "src/content/_templates/project-template.mdx",
    fields: [
      { name: "title", label: "Title", kind: "text", required: true },
      {
        name: "description",
        label: "Description",
        kind: "textarea",
        required: true,
      },
      { name: "pubDate", label: "Publish date", kind: "date", required: true },
      { name: "updatedDate", label: "Updated date", kind: "date" },
      {
        name: "category",
        label: "Category",
        kind: "select",
        required: true,
        options: CONTENT_CATEGORIES,
      },
      {
        name: "tags",
        label: "Tags",
        kind: "tags",
        help: `Suggested: ${CONTENT_TAGS.slice(0, 5).join(", ")}...`,
      },
      {
        name: "difficulty",
        label: "Difficulty",
        kind: "select",
        options: ["", "easy", "medium", "hard"],
      },
      { name: "estimatedCost", label: "Estimated cost", kind: "text" },
      { name: "estimatedTime", label: "Estimated time", kind: "text" },
      { name: "githubUrl", label: "GitHub URL", kind: "text" },
      {
        name: "partsList",
        label: "Parts list",
        kind: "textarea",
        help: "YAML array or leave blank.",
      },
      { name: "heroImage", label: "Hero image", kind: "text" },
      { name: "imageAlt", label: "Image alt text", kind: "textarea" },
      {
        name: "imageAttributionName",
        label: "Image attribution name",
        kind: "text",
        help: "Photographer or source name",
      },
      {
        name: "imageAttributionUrl",
        label: "Image attribution URL",
        kind: "text",
        help: "Link to image source (optional)",
      },
      {
        name: "preview",
        label: "Preview/excerpt",
        kind: "textarea",
        help: "Short text for post listings",
      },
      { name: "draft", label: "Draft", kind: "checkbox" },
      ...llmFields,
    ],
  },
  "field-notes": {
    key: "field-notes",
    label: "Field note",
    folder: "field-notes",
    templatePath: "src/content/_templates/field-note-template.mdx",
    fields: [
      { name: "title", label: "Title", kind: "text", required: true },
      {
        name: "description",
        label: "Description",
        kind: "textarea",
        required: true,
      },
      { name: "pubDate", label: "Publish date", kind: "date", required: true },
      { name: "location", label: "Location", kind: "text", required: true },
      { name: "region", label: "Region", kind: "text" },
      { name: "weather", label: "Weather", kind: "text" },
      {
        name: "category",
        label: "Category",
        kind: "select",
        required: true,
        options: CONTENT_CATEGORIES,
      },
      {
        name: "tags",
        label: "Tags",
        kind: "tags",
        help: `Suggested: ${CONTENT_TAGS.slice(0, 5).join(", ")}...`,
      },
      {
        name: "includesHomeschool",
        label: "Includes homeschool",
        kind: "checkbox",
      },
      { name: "heroImage", label: "Hero image", kind: "text" },
      { name: "imageAlt", label: "Image alt text", kind: "textarea" },
      {
        name: "imageAttributionName",
        label: "Image attribution name",
        kind: "text",
        help: "Photographer or source name",
      },
      {
        name: "imageAttributionUrl",
        label: "Image attribution URL",
        kind: "text",
        help: "Link to image source (optional)",
      },
      {
        name: "preview",
        label: "Preview/excerpt",
        kind: "textarea",
        help: "Short text for post listings",
      },
      { name: "draft", label: "Draft", kind: "checkbox" },
      ...llmFields,
    ],
  },
};

export function isCollectionKey(value: string): value is CollectionKey {
  return value === "blog" || value === "books" || value === "projects" ||
    value === "field-notes";
}
