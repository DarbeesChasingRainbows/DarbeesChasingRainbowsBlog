#!/usr/bin/env node
/**
 * #7 image helper — watch an inbox folder; on a dropped photo, convert it,
 * name it, place it under src/assets/, write AI alt text, print a Markdown
 * snippet, and delete the source.
 *
 * This file exports its pure helpers (for tests) and runs the watcher when
 * invoked directly. Orchestration lives below the helpers.
 */
import { relative, sep, join, extname, basename } from 'node:path';

export const KNOWN_COLLECTIONS = ['blog', 'projects', 'field-notes'];
const WEB_FORMATS = new Set(['.jpg', '.jpeg', '.png', '.webp']);
const HEIC_FORMATS = new Set(['.heic', '.heif']);

/** inbox/<collection>/<slug>/<file> -> { collection, slug, filename } or null. */
export function deriveCollectionSlug(inboxRoot, filePath) {
	const parts = relative(inboxRoot, filePath).split(sep);
	if (parts.length !== 3) return null;
	const [collection, slug, filename] = parts;
	if (!KNOWN_COLLECTIONS.includes(collection)) return null;
	return { collection, slug, filename };
}

/** Lowercase, spaces -> hyphens, strip non [a-z0-9-]; empty base falls back to "image". */
export function sanitizeName(filename) {
	const ext = extname(filename).toLowerCase();
	const base = basename(filename, extname(filename))
		.toLowerCase()
		.replace(/\s+/g, '-')
		.replace(/[^a-z0-9-]/g, '')
		.replace(/-+/g, '-')
		.replace(/^-|-$/g, '');
	return { base: base || 'image', ext };
}

/** 'heic' (needs transcode), 'web' (copy as-is), or 'unsupported'. */
export function classifyFormat(ext) {
	const e = ext.toLowerCase();
	if (HEIC_FORMATS.has(e)) return 'heic';
	if (WEB_FORMATS.has(e)) return 'web';
	return 'unsupported';
}

/** Pick an output path, suffixing -2, -3... on collision. `exists` is injectable. */
export function resolveOutputPath(dir, base, ext, exists) {
	let candidate = join(dir, `${base}${ext}`);
	let n = 2;
	while (exists(candidate)) {
		candidate = join(dir, `${base}-${n}${ext}`);
		n++;
	}
	return candidate;
}

/** The ready-to-paste Markdown image tag (paths are relative to src/content/<collection>/). */
export function buildMarkdownSnippet(collection, slug, filename, altText) {
	return `![${altText}](../../assets/${collection}/${slug}/${filename})`;
}
