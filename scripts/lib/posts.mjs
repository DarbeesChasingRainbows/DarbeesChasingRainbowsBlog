/**
 * Walks the Astro content collections, parses frontmatter, and produces the
 * text + hash used for embedding. Knows the content layout, nothing about LM Studio.
 */
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { createHash } from 'node:crypto';
import matter from 'gray-matter';

/** Collections that participate in related posts (#6). */
export const PRIMARY_COLLECTIONS = ['blog', 'projects', 'field-notes'];
/** Collections that participate in GEO fill (#5) — includes books. */
export const ALL_COLLECTIONS = ['blog', 'projects', 'field-notes', 'books'];

/** Astro-style id: path under the collection root, posix separators, no .mdx. */
export function deriveId(collectionRoot, filePath) {
	return relative(collectionRoot, filePath)
		.split(sep)
		.join('/')
		.replace(/\.mdx$/, '');
}

/** Reduce MDX to a plain-text approximation for embedding. Naive on purpose. */
export function stripMdx(body) {
	return body
		.replace(/^import\s.+$/gm, '') // import lines
		.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1') // links -> link text
		.replace(/<[^>]+>/g, '') // JSX / HTML tags
		.replace(/[#*_`>|~-]/g, ' ') // markdown punctuation
		.replace(/\s+/g, ' ')
		.trim();
}

/** The text we embed for similarity: identity fields + stripped body. */
export function embedText(post) {
	const { title = '', description = '', tags = [], category = '' } = post.frontmatter;
	return [
		title,
		description,
		`Tags: ${tags.join(', ')}`,
		`Category: ${category}`,
		'',
		stripMdx(post.body),
	].join('\n');
}

/** Stable sha256 of exactly what gets embedded — the cache-key base. */
export function contentHash(post) {
	return createHash('sha256').update(embedText(post)).digest('hex');
}

async function walkMdx(dir) {
	const out = [];
	let entries;
	try {
		entries = await readdir(dir, { withFileTypes: true });
	} catch {
		return out; // collection dir absent — fine
	}
	for (const entry of entries) {
		if (entry.name === '_templates') continue;
		const full = join(dir, entry.name);
		if (entry.isDirectory()) {
			out.push(...(await walkMdx(full)));
		} else if (entry.name.endsWith('.mdx')) {
			out.push(full);
		}
	}
	return out;
}

/**
 * Walk content collections and return parsed posts.
 * @returns {Promise<Array<{collection,id,path,frontmatter,body}>>}
 */
export async function listPosts({
	contentRoot = 'src/content',
	collections = PRIMARY_COLLECTIONS,
	includeDrafts = false,
} = {}) {
	const posts = [];
	for (const collection of collections) {
		const root = join(contentRoot, collection);
		for (const path of await walkMdx(root)) {
			const raw = await readFile(path, 'utf8');
			const { data: frontmatter, content: body } = matter(raw);
			if (!includeDrafts && frontmatter.draft === true) continue;
			posts.push({ collection, id: deriveId(root, path), path, frontmatter, body });
		}
	}
	return posts;
}
