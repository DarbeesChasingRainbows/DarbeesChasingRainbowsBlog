import {
  dirname,
  fromFileUrl,
  join,
  normalize,
  relative,
} from "jsr:@std/path@^1.1.2";
import { type CollectionKey, collections, isCollectionKey } from "./schemas.ts";
import {
  type Frontmatter,
  type MdxDocument,
  serializeMdx,
  splitMdx,
} from "./frontmatter.ts";

export type { CollectionKey };

export interface ContentEntry {
  collection: CollectionKey;
  slug: string;
  title: string;
  description: string;
  draft: boolean;
  pubDate: string;
}

const cmsRoot = dirname(fromFileUrl(import.meta.url));
const repoRoot = normalize(join(cmsRoot, "..", ".."));
const contentRoot = join(repoRoot, "src", "content");
const slugPattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export function assertCollection(value: string): CollectionKey {
  if (!isCollectionKey(value)) {
    throw new Error(`Unknown collection: ${value}`);
  }
  return value;
}

export function assertSlug(value: string): string {
  const slug = value.trim().toLowerCase();
  if (!slugPattern.test(slug)) {
    throw new Error(
      "Slug must be kebab-case using lowercase letters, numbers, and hyphens.",
    );
  }
  return slug;
}

export async function listEntries(
  collection?: CollectionKey,
): Promise<ContentEntry[]> {
  const keys = collection
    ? [collection]
    : Object.keys(collections) as CollectionKey[];
  const entries: ContentEntry[] = [];

  for (const key of keys) {
    const def = collections[key];
    const dir = collectionDirectory(key);

    try {
      for await (const item of Deno.readDir(dir)) {
        if (!item.isFile || !item.name.endsWith(".mdx")) continue;
        if (
          item.name.endsWith(".draft.mdx") || item.name.endsWith(".wip.mdx")
        ) continue;
        const slug = item.name.replace(/\.mdx$/, "");
        const doc = await readEntry(key, slug);
        entries.push({
          collection: key,
          slug,
          title: String(doc.frontmatter.title ?? slug),
          description: String(doc.frontmatter.description ?? ""),
          draft: doc.frontmatter.draft === true,
          pubDate: String(doc.frontmatter.pubDate ?? ""),
        });
      }
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
      await Deno.mkdir(dir, { recursive: true });
    }

    void def;
  }

  return entries.sort((a, b) => b.pubDate.localeCompare(a.pubDate));
}

export async function listDrafts(
  collection?: CollectionKey,
): Promise<ContentEntry[]> {
  const keys = collection
    ? [collection]
    : Object.keys(collections) as CollectionKey[];
  const entries: ContentEntry[] = [];

  for (const key of keys) {
    const dir = collectionDirectory(key);

    try {
      for await (const item of Deno.readDir(dir)) {
        if (!item.isFile || !item.name.endsWith(".mdx")) continue;
        if (
          item.name.endsWith(".draft.mdx") || item.name.endsWith(".wip.mdx")
        ) continue;
        const slug = item.name.replace(/\.mdx$/, "");
        const doc = await readEntry(key, slug);
        if (doc.frontmatter.draft === true) {
          entries.push({
            collection: key,
            slug,
            title: String(doc.frontmatter.title ?? slug),
            description: String(doc.frontmatter.description ?? ""),
            draft: true,
            pubDate: String(doc.frontmatter.pubDate ?? ""),
          });
        }
      }
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
    }
  }

  return entries.sort((a, b) => b.pubDate.localeCompare(a.pubDate));
}

export async function readEntry(
  collection: CollectionKey,
  slug: string,
): Promise<MdxDocument> {
  const path = entryPath(collection, assertSlug(slug));
  const source = await Deno.readTextFile(path);
  return splitMdx(source);
}

export async function readTemplate(
  collection: CollectionKey,
): Promise<MdxDocument> {
  const def = collections[collection];
  const source = await Deno.readTextFile(join(repoRoot, def.templatePath));
  return splitMdx(source);
}

export async function writeEntry(
  collection: CollectionKey,
  slug: string,
  frontmatter: Frontmatter,
  body: string,
  overwrite = false,
): Promise<void> {
  const safeSlug = assertSlug(slug);
  const path = entryPath(collection, safeSlug);

  if (!overwrite) {
    try {
      await Deno.stat(path);
      throw new Error(
        `A ${collection} entry already exists for slug ${safeSlug}.`,
      );
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
    }
  }

  await Deno.mkdir(dirname(path), { recursive: true });
  await Deno.writeTextFile(path, serializeMdx(frontmatter, body));
}

export interface ContentVersion {
  hash: string;
  message: string;
  author: string;
  date: string;
}

export async function getEntryHistory(
  collection: CollectionKey,
  slug: string,
): Promise<ContentVersion[]> {
  const path = entryPath(collection, assertSlug(slug));

  try {
    const cmd = new Deno.Command("git", {
      args: [
        "log",
        "--format=%H|%s|%an|%ai",
        "--",
        path,
      ],
      cwd: repoRoot,
    });
    const { code, stdout } = await cmd.output();

    if (code !== 0) {
      throw new Error(`git log failed with code ${code}`);
    }

    const output = new TextDecoder().decode(stdout);
    const versions: ContentVersion[] = [];

    for (const line of output.trim().split("\n")) {
      if (!line) continue;
      const [hash, message, author, date] = line.split("|");
      versions.push({ hash, message, author, date });
    }

    return versions;
  } catch (error) {
    if (error instanceof Deno.errors.NotFound) {
      // git not available or not a git repo
      return [];
    }
    throw error;
  }
}

export async function restoreEntryVersion(
  collection: CollectionKey,
  slug: string,
  hash: string,
): Promise<void> {
  const path = entryPath(collection, assertSlug(slug));

  const cmd = new Deno.Command("git", {
    args: [
      "checkout",
      hash,
      "--",
      path,
    ],
    cwd: repoRoot,
  });

  const { code } = await cmd.output();

  if (code !== 0) {
    throw new Error(`git checkout failed with code ${code}`);
  }
}

export function collectionDirectory(collection: CollectionKey): string {
  const dir = join(contentRoot, collections[collection].folder);
  assertInsideContent(dir);
  return dir;
}

function entryPath(collection: CollectionKey, slug: string): string {
  const path = join(collectionDirectory(collection), `${slug}.mdx`);
  assertInsideContent(path);
  return path;
}

function assertInsideContent(path: string): void {
  const normalized = normalize(path);
  const rel = relative(contentRoot, normalized);
  if (rel.startsWith("..") || rel.startsWith("/") || rel.startsWith("\\")) {
    throw new Error("Refusing to access files outside src/content.");
  }
}
