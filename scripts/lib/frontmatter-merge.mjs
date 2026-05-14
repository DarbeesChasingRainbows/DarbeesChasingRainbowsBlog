/**
 * Non-destructive frontmatter merge + serialize. Pure functions, no I/O.
 */
import matter from 'gray-matter';

/** A value is "empty" (safe to fill) when undefined, null, blank string, or []. */
export function isEmpty(value) {
	if (value === undefined || value === null) return true;
	if (typeof value === 'string') return value.trim() === '';
	if (Array.isArray(value)) return value.length === 0;
	return false;
}

/**
 * Merge generated frontmatter into existing.
 * - force: overwrite existing keys.
 * - default: write a generated key only when the existing value isEmpty().
 * Existing key order is preserved; brand-new keys are appended.
 * @returns {{ merged: object, changedKeys: string[] }}
 */
export function mergeFrontmatter(existing, generated, { force = false } = {}) {
	const merged = { ...existing };
	const changedKeys = [];
	for (const [key, value] of Object.entries(generated)) {
		if (!force && !isEmpty(existing[key])) continue;
		merged[key] = value;
		changedKeys.push(key);
	}
	return { merged, changedKeys };
}

/** Re-emit an .mdx file: YAML frontmatter block + body. Body passed through as-is. */
export function serialize(frontmatter, body) {
	return matter.stringify(body, frontmatter);
}
