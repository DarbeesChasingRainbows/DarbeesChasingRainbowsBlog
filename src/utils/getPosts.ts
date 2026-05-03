import { getCollection, type CollectionEntry } from 'astro:content';

/**
 * Get all published (non-draft) blog posts, sorted by pubDate descending.
 */
export async function getBlogPosts(): Promise<CollectionEntry<'blog'>[]> {
	const posts = await getCollection('blog', ({ data }) => !data.draft || import.meta.env.DEV);
	return posts.sort(
		(a, b) => new Date(b.data.pubDate).valueOf() - new Date(a.data.pubDate).valueOf(),
	);
}

/**
 * Get all published projects, sorted by pubDate descending.
 */
export async function getProjects(): Promise<CollectionEntry<'projects'>[]> {
	const posts = await getCollection('projects', ({ data }) => !data.draft || import.meta.env.DEV);
	return posts.sort(
		(a, b) => new Date(b.data.pubDate).valueOf() - new Date(a.data.pubDate).valueOf(),
	);
}

/**
 * Get all published field notes, sorted by pubDate descending.
 */
export async function getFieldNotes(): Promise<CollectionEntry<'field-notes'>[]> {
	const posts = await getCollection(
		'field-notes',
		({ data }) => !data.draft || import.meta.env.DEV,
	);
	return posts.sort(
		(a, b) => new Date(b.data.pubDate).valueOf() - new Date(a.data.pubDate).valueOf(),
	);
}

/**
 * Get the latest N entries across all three collections combined.
 */
export async function getLatestAll(limit = 6) {
	const [blog, projects, fieldNotes] = await Promise.all([
		getBlogPosts(),
		getProjects(),
		getFieldNotes(),
	]);

	const all = [
		...blog.map((p) => ({ ...p, _type: 'blog' as const })),
		...projects.map((p) => ({ ...p, _type: 'projects' as const })),
		...fieldNotes.map((p) => ({ ...p, _type: 'field-notes' as const })),
	];

	return all
		.sort(
			(a, b) =>
				new Date(b.data.pubDate).valueOf() - new Date(a.data.pubDate).valueOf(),
		)
		.slice(0, limit);
}

/**
 * Get unique categories from a list of entries.
 */
export function getCategories<T extends { data: { category: string } }>(entries: T[]): string[] {
	const set = new Set(entries.map((e) => e.data.category));
	return Array.from(set).sort();
}

/**
 * Get unique tags from a list of entries.
 */
export function getTags<T extends { data: { tags: string[] } }>(entries: T[]): string[] {
	const set = new Set(entries.flatMap((e) => e.data.tags));
	return Array.from(set).sort();
}

/**
 * Get the URL path for a content type.
 */
export function getEntryUrl(type: 'blog' | 'projects' | 'field-notes', id: string): string {
	return `/${type}/${id}/`;
}

/**
 * Given a list of entries (already sorted newest-first) and the current entry id,
 * return the chronologically adjacent entries.
 *  - `prev` is the older post (later in the array)
 *  - `next` is the newer post (earlier in the array)
 */
export function getAdjacentPosts<T extends { id: string }>(
	entries: T[],
	currentId: string,
): { prev?: T; next?: T } {
	const idx = entries.findIndex((e) => e.id === currentId);
	if (idx === -1) return {};
	return {
		next: idx > 0 ? entries[idx - 1] : undefined,
		prev: idx < entries.length - 1 ? entries[idx + 1] : undefined,
	};
}

/**
 * Find related entries — same category first, then same tag — excluding the current entry.
 */
export function getRelatedPosts<
	T extends { id: string; data: { category: string; tags?: string[] } },
>(entries: T[], current: T, limit = 2): T[] {
	const others = entries.filter((e) => e.id !== current.id);
	const sameCategory = others.filter((e) => e.data.category === current.data.category);
	const currentTags = new Set(current.data.tags ?? []);
	const tagMatches = others.filter(
		(e) =>
			e.data.category !== current.data.category &&
			(e.data.tags ?? []).some((t) => currentTags.has(t)),
	);
	const seen = new Set<string>();
	const merged: T[] = [];
	for (const e of [...sameCategory, ...tagMatches, ...others]) {
		if (seen.has(e.id)) continue;
		seen.add(e.id);
		merged.push(e);
		if (merged.length >= limit) break;
	}
	return merged;
}
