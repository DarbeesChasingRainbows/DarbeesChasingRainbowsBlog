import { b as assertPath, f as fromFileUrl, i as isPosixPathSeparator, d as fromFileUrl$1, e as isPathSeparator, g as isWindowsDeviceRoot, C as CHAR_COLON, h as isPosixPathSeparator$1, j as isWindows, n as normalize$1, k as normalize$2, m as join, r as relative, o as fromFileUrl$2 } from "../server-entry.js";
function stripTrailingSeparators(segment, isSep) {
  if (segment.length <= 1) {
    return segment;
  }
  let end = segment.length;
  for (let i = segment.length - 1; i > 0; i--) {
    if (isSep(segment.charCodeAt(i))) {
      end = i;
    } else {
      break;
    }
  }
  return segment.slice(0, end);
}
function assertArg(path) {
  assertPath(path);
  if (path.length === 0) return ".";
}
function dirname$2(path) {
  if (path instanceof URL) {
    path = fromFileUrl(path);
  }
  assertArg(path);
  let end = -1;
  let matchedNonSeparator = false;
  for (let i = path.length - 1; i >= 1; --i) {
    if (isPosixPathSeparator(path.charCodeAt(i))) {
      if (matchedNonSeparator) {
        end = i;
        break;
      }
    } else {
      matchedNonSeparator = true;
    }
  }
  if (end === -1) {
    return isPosixPathSeparator(path.charCodeAt(0)) ? "/" : ".";
  }
  return stripTrailingSeparators(path.slice(0, end), isPosixPathSeparator);
}
function dirname$1(path) {
  if (path instanceof URL) {
    path = fromFileUrl$1(path);
  }
  assertArg(path);
  const len = path.length;
  let rootEnd = -1;
  let end = -1;
  let matchedSlash = true;
  let offset = 0;
  const code = path.charCodeAt(0);
  if (len > 1) {
    if (isPathSeparator(code)) {
      rootEnd = offset = 1;
      if (isPathSeparator(path.charCodeAt(1))) {
        let j = 2;
        let last = j;
        for (; j < len; ++j) {
          if (isPathSeparator(path.charCodeAt(j))) break;
        }
        if (j < len && j !== last) {
          last = j;
          for (; j < len; ++j) {
            if (!isPathSeparator(path.charCodeAt(j))) break;
          }
          if (j < len && j !== last) {
            last = j;
            for (; j < len; ++j) {
              if (isPathSeparator(path.charCodeAt(j))) break;
            }
            if (j === len) {
              return path;
            }
            if (j !== last) {
              rootEnd = offset = j + 1;
            }
          }
        }
      }
    } else if (isWindowsDeviceRoot(code)) {
      if (path.charCodeAt(1) === CHAR_COLON) {
        rootEnd = offset = 2;
        if (len > 2) {
          if (isPathSeparator(path.charCodeAt(2))) rootEnd = offset = 3;
        }
      }
    }
  } else if (isPathSeparator(code)) {
    return path;
  }
  for (let i = len - 1; i >= offset; --i) {
    if (isPathSeparator(path.charCodeAt(i))) {
      if (!matchedSlash) {
        end = i;
        break;
      }
    } else {
      matchedSlash = false;
    }
  }
  if (end === -1) {
    if (rootEnd === -1) return ".";
    else end = rootEnd;
  }
  return stripTrailingSeparators(path.slice(0, end), isPosixPathSeparator$1);
}
function dirname(path) {
  return isWindows ? dirname$1(path) : dirname$2(path);
}
function normalize(path) {
  return isWindows ? normalize$1(path) : normalize$2(path);
}
const llmFields = [{
  name: "aiSummary",
  label: "AI summary",
  kind: "textarea"
}, {
  name: "keyTakeaways",
  label: "Key takeaways",
  kind: "list",
  help: "One takeaway per line."
}, {
  name: "entityMentions",
  label: "Entity mentions",
  kind: "list",
  help: "One entity per line."
}, {
  name: "faq",
  label: "FAQ",
  kind: "textarea",
  help: "YAML array or leave blank."
}, {
  name: "sources",
  label: "Sources",
  kind: "textarea",
  help: "YAML array or leave blank."
}];
const collections = {
  blog: {
    key: "blog",
    label: "Blog post",
    folder: "blog",
    templatePath: "src/content/_templates/blog-post-template.mdx",
    fields: [{
      name: "title",
      label: "Title",
      kind: "text",
      required: true
    }, {
      name: "description",
      label: "Description",
      kind: "textarea",
      required: true
    }, {
      name: "pubDate",
      label: "Publish date",
      kind: "date",
      required: true
    }, {
      name: "updatedDate",
      label: "Updated date",
      kind: "date"
    }, {
      name: "author",
      label: "Author",
      kind: "text"
    }, {
      name: "category",
      label: "Category",
      kind: "text",
      required: true
    }, {
      name: "tags",
      label: "Tags",
      kind: "tags"
    }, {
      name: "heroImage",
      label: "Hero image",
      kind: "text"
    }, {
      name: "imageAlt",
      label: "Image alt text",
      kind: "textarea"
    }, {
      name: "draft",
      label: "Draft",
      kind: "checkbox"
    }, ...llmFields]
  },
  books: {
    key: "books",
    label: "Book review",
    folder: "books",
    templatePath: "src/content/_templates/book-template.mdx",
    fields: [{
      name: "title",
      label: "Page title",
      kind: "text",
      required: true
    }, {
      name: "description",
      label: "Description",
      kind: "textarea",
      required: true
    }, {
      name: "bookTitle",
      label: "Book title",
      kind: "text",
      required: true
    }, {
      name: "author",
      label: "Author",
      kind: "text",
      required: true
    }, {
      name: "pubDate",
      label: "Publish date",
      kind: "date",
      required: true
    }, {
      name: "category",
      label: "Category",
      kind: "select",
      required: true,
      options: ["Kids", "Tweens", "Teens", "Family", "Homeschool", "Kingdom Farm", "Theology", "Nature", "History"]
    }, {
      name: "ageRange",
      label: "Age range",
      kind: "text"
    }, {
      name: "formatUsed",
      label: "Format used",
      kind: "select",
      options: ["", "read-aloud", "audiobook", "independent", "mixed"]
    }, {
      name: "rating",
      label: "Rating",
      kind: "select",
      required: true,
      options: ["green", "yellow", "parent-read", "red"]
    }, {
      name: "tags",
      label: "Tags",
      kind: "tags"
    }, {
      name: "heroImage",
      label: "Hero image",
      kind: "text"
    }, {
      name: "draft",
      label: "Draft",
      kind: "checkbox"
    }, {
      name: "dadTake",
      label: "Dad take",
      kind: "textarea"
    }, {
      name: "momTake",
      label: "Mom take",
      kind: "textarea"
    }, {
      name: "kidsTake",
      label: "Kids take",
      kind: "textarea"
    }, {
      name: "readAloudValue",
      label: "Read-aloud value",
      kind: "textarea"
    }, {
      name: "audiobookValue",
      label: "Audiobook value",
      kind: "textarea"
    }, {
      name: "educationalValue",
      label: "Educational value",
      kind: "textarea"
    }, {
      name: "contentNotes",
      label: "Content notes",
      kind: "textarea"
    }, {
      name: "worldviewNotes",
      label: "Worldview notes",
      kind: "textarea"
    }, ...llmFields]
  },
  projects: {
    key: "projects",
    label: "Project",
    folder: "projects",
    templatePath: "src/content/_templates/project-template.mdx",
    fields: [{
      name: "title",
      label: "Title",
      kind: "text",
      required: true
    }, {
      name: "description",
      label: "Description",
      kind: "textarea",
      required: true
    }, {
      name: "pubDate",
      label: "Publish date",
      kind: "date",
      required: true
    }, {
      name: "updatedDate",
      label: "Updated date",
      kind: "date"
    }, {
      name: "category",
      label: "Category",
      kind: "text",
      required: true
    }, {
      name: "tags",
      label: "Tags",
      kind: "tags"
    }, {
      name: "difficulty",
      label: "Difficulty",
      kind: "select",
      options: ["", "easy", "medium", "hard"]
    }, {
      name: "estimatedCost",
      label: "Estimated cost",
      kind: "text"
    }, {
      name: "estimatedTime",
      label: "Estimated time",
      kind: "text"
    }, {
      name: "githubUrl",
      label: "GitHub URL",
      kind: "text"
    }, {
      name: "partsList",
      label: "Parts list",
      kind: "textarea",
      help: "YAML array or leave blank."
    }, {
      name: "heroImage",
      label: "Hero image",
      kind: "text"
    }, {
      name: "imageAlt",
      label: "Image alt text",
      kind: "textarea"
    }, {
      name: "draft",
      label: "Draft",
      kind: "checkbox"
    }, ...llmFields]
  },
  "field-notes": {
    key: "field-notes",
    label: "Field note",
    folder: "field-notes",
    templatePath: "src/content/_templates/field-note-template.mdx",
    fields: [{
      name: "title",
      label: "Title",
      kind: "text",
      required: true
    }, {
      name: "description",
      label: "Description",
      kind: "textarea",
      required: true
    }, {
      name: "pubDate",
      label: "Publish date",
      kind: "date",
      required: true
    }, {
      name: "location",
      label: "Location",
      kind: "text",
      required: true
    }, {
      name: "region",
      label: "Region",
      kind: "text"
    }, {
      name: "weather",
      label: "Weather",
      kind: "text"
    }, {
      name: "category",
      label: "Category",
      kind: "text",
      required: true
    }, {
      name: "tags",
      label: "Tags",
      kind: "tags"
    }, {
      name: "includesHomeschool",
      label: "Includes homeschool",
      kind: "checkbox"
    }, {
      name: "heroImage",
      label: "Hero image",
      kind: "text"
    }, {
      name: "imageAlt",
      label: "Image alt text",
      kind: "textarea"
    }, {
      name: "draft",
      label: "Draft",
      kind: "checkbox"
    }, ...llmFields]
  }
};
function isCollectionKey(value) {
  return value === "blog" || value === "books" || value === "projects" || value === "field-notes";
}
function splitMdx(source) {
  if (!source.startsWith("---\n")) {
    return {
      frontmatter: {},
      body: source
    };
  }
  const end = source.indexOf("\n---", 4);
  if (end === -1) {
    return {
      frontmatter: {},
      body: source
    };
  }
  const yaml = source.slice(4, end).trim();
  const body = source.slice(end + 5).replace(/^\r?\n/, "");
  return {
    frontmatter: parseSimpleYaml(yaml),
    body
  };
}
function parseSimpleYaml(yaml) {
  const result = {};
  const lines = yaml.split(/\r?\n/);
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (!line.trim() || line.trim().startsWith("#") || line.startsWith(" ")) {
      continue;
    }
    const match = line.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
    if (!match) {
      continue;
    }
    const [, key, rawValue] = match;
    const following = [];
    let cursor = index + 1;
    while (cursor < lines.length && /^\s+/.test(lines[cursor])) {
      following.push(lines[cursor]);
      cursor++;
    }
    if (following.length > 0 && rawValue.trim() === "") {
      result[key] = parseBlockValue(following);
      index = cursor - 1;
      continue;
    }
    if (rawValue.trim() === ">-") {
      result[key] = following.map((item) => item.trim()).join(" ").trim();
      index = cursor - 1;
      continue;
    }
    result[key] = parseScalar(rawValue.trim());
  }
  return result;
}
function parseBlockValue(lines) {
  const trimmed = lines.map((line) => line.trim()).filter(Boolean);
  if (trimmed.every((line) => line.startsWith("- "))) {
    return trimmed.map((line) => stripQuotes(line.slice(2).trim()));
  }
  return trimmed.join("\n");
}
function parseScalar(value) {
  if (value === "true") return true;
  if (value === "false") return false;
  if (value.startsWith("[") && value.endsWith("]")) {
    return value.slice(1, -1).split(",").map((item) => stripQuotes(item.trim())).filter(Boolean);
  }
  return stripQuotes(value.replace(/\s+#.*$/, ""));
}
function stripQuotes(value) {
  if (value.startsWith("'") && value.endsWith("'") || value.startsWith('"') && value.endsWith('"')) {
    return value.slice(1, -1);
  }
  return value;
}
function serializeMdx(frontmatter, body) {
  return `---
${serializeFrontmatter(frontmatter)}---

${body.trim()}
`;
}
function serializeFrontmatter(frontmatter) {
  const lines = [];
  for (const [key, value] of Object.entries(frontmatter)) {
    if (value === void 0 || value === "" || Array.isArray(value) && value.length === 0) {
      continue;
    }
    if (typeof value === "boolean" || typeof value === "number") {
      lines.push(`${key}: ${value}`);
      continue;
    }
    if (Array.isArray(value)) {
      lines.push(`${key}:`);
      for (const item of value) {
        if (typeof item === "string") {
          lines.push(`  - ${quoteYaml(item)}`);
        } else {
          lines.push(`  - ${JSON.stringify(item)}`);
        }
      }
      continue;
    }
    if (typeof value === "string" && value.includes("\n")) {
      lines.push(`${key}: >-`);
      for (const line of value.split(/\r?\n/)) {
        lines.push(`  ${line}`);
      }
      continue;
    }
    lines.push(`${key}: ${quoteYaml(String(value))}`);
  }
  return `${lines.join("\n")}
`;
}
function quoteYaml(value) {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return value;
  }
  const escaped = value.replaceAll("'", "''");
  return `'${escaped}'`;
}
const cmsRoot = dirname(fromFileUrl$2(import.meta.url));
const repoRoot = normalize(join(cmsRoot, "..", ".."));
const contentRoot = join(repoRoot, "src", "content");
const slugPattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
function assertCollection(value) {
  if (!isCollectionKey(value)) {
    throw new Error(`Unknown collection: ${value}`);
  }
  return value;
}
function assertSlug(value) {
  const slug = value.trim().toLowerCase();
  if (!slugPattern.test(slug)) {
    throw new Error("Slug must be kebab-case using lowercase letters, numbers, and hyphens.");
  }
  return slug;
}
async function listEntries(collection) {
  const keys = Object.keys(collections);
  const entries = [];
  for (const key of keys) {
    const dir = collectionDirectory(key);
    try {
      for await (const item of Deno.readDir(dir)) {
        if (!item.isFile || !item.name.endsWith(".mdx")) continue;
        if (item.name.endsWith(".draft.mdx") || item.name.endsWith(".wip.mdx")) continue;
        const slug = item.name.replace(/\.mdx$/, "");
        const doc = await readEntry(key, slug);
        entries.push({
          collection: key,
          slug,
          title: String(doc.frontmatter.title ?? slug),
          description: String(doc.frontmatter.description ?? ""),
          draft: doc.frontmatter.draft === true,
          pubDate: String(doc.frontmatter.pubDate ?? "")
        });
      }
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
      await Deno.mkdir(dir, {
        recursive: true
      });
    }
  }
  return entries.sort((a, b) => b.pubDate.localeCompare(a.pubDate));
}
async function readEntry(collection, slug) {
  const path = entryPath(collection, assertSlug(slug));
  const source = await Deno.readTextFile(path);
  return splitMdx(source);
}
async function readTemplate(collection) {
  const def = collections[collection];
  const source = await Deno.readTextFile(join(repoRoot, def.templatePath));
  return splitMdx(source);
}
async function writeEntry(collection, slug, frontmatter, body, overwrite = false) {
  const safeSlug = assertSlug(slug);
  const path = entryPath(collection, safeSlug);
  if (!overwrite) {
    try {
      await Deno.stat(path);
      throw new Error(`A ${collection} entry already exists for slug ${safeSlug}.`);
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
    }
  }
  await Deno.mkdir(dirname(path), {
    recursive: true
  });
  await Deno.writeTextFile(path, serializeMdx(frontmatter, body));
}
function collectionDirectory(collection) {
  const dir = join(contentRoot, collections[collection].folder);
  assertInsideContent(dir);
  return dir;
}
function entryPath(collection, slug) {
  const path = join(collectionDirectory(collection), `${slug}.mdx`);
  assertInsideContent(path);
  return path;
}
function assertInsideContent(path) {
  const normalized = normalize(path);
  const rel = relative(contentRoot, normalized);
  if (rel.startsWith("..") || rel.startsWith("/") || rel.startsWith("\\")) {
    throw new Error("Refusing to access files outside src/content.");
  }
}
export {
  assertCollection as a,
  assertSlug as b,
  collections as c,
  readTemplate as d,
  listEntries as l,
  readEntry as r,
  writeEntry as w
};
