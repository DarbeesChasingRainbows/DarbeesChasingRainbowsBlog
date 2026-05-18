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

// ---------------------------------------------------------------------------
// Orchestration — runs only when this file is invoked directly.
// ---------------------------------------------------------------------------
import { readFile, writeFile, mkdir, rm, access } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { watch } from 'chokidar';
import sharp from 'sharp';
import { createClient } from './lib/openai-compatible.mjs';

const INBOX_ROOT = 'obsidian-templates/inbox';
const ASSETS_ROOT = 'src/assets';
const CONTENT_ROOT = 'src/content';

const ALT_SCHEMA = {
	name: 'image_alt',
	required: ['altText'],
	shape: {
		type: 'object',
		properties: { altText: { type: 'string' } },
		required: ['altText'],
	},
};

async function postExists(collection, slug) {
	try {
		await access(join(CONTENT_ROOT, collection, `${slug}.mdx`));
		return true;
	} catch {
		return false;
	}
}

async function processFile(client, filePath) {
	const derived = deriveCollectionSlug(INBOX_ROOT, filePath);
	if (!derived) {
		console.error(`✗ ${filePath} — expected inbox/<collection>/<slug>/<file>`);
		return;
	}
	const { collection, slug, filename } = derived;
	if (!(await postExists(collection, slug))) {
		console.error(`✗ ${filePath} — no post at ${collection}/${slug}.mdx`);
		return;
	}

	const { base, ext } = sanitizeName(filename);
	const format = classifyFormat(ext);
	if (format === 'unsupported') {
		console.error(`✗ ${filePath} — unsupported image format ${ext}`);
		return;
	}

	const sourceBuffer = await readFile(filePath);
	// A JPEG buffer is always produced for the vision payload.
	const jpegBuffer = await sharp(sourceBuffer).jpeg({ quality: 90 }).toBuffer();
	// Output: transcoded JPEG for HEIC, original bytes for web formats.
	const outBuffer = format === 'heic' ? jpegBuffer : sourceBuffer;
	const outExt = format === 'heic' ? '.jpg' : ext;

	const postBody = await readFile(join(CONTENT_ROOT, collection, `${slug}.mdx`), 'utf8');
	const prompt =
		'Write concise alt text describing what is literally visible in this image ' +
		'(not what the post is about). One sentence. Context from the post:\n\n' +
		postBody.slice(0, 1500);
	const { altText } = await client.vision(jpegBuffer, prompt, ALT_SCHEMA);

	const outDir = join(ASSETS_ROOT, collection, slug);
	await mkdir(outDir, { recursive: true });
	const outPath = resolveOutputPath(outDir, base, outExt, existsSync);
	await writeFile(outPath, outBuffer);

	const outName = basename(outPath);
	console.log(`✓ ${filePath}`);
	console.log(`  → ${outPath}`);
	console.log(`  ${buildMarkdownSnippet(collection, slug, outName, altText)}`);
	await rm(filePath);
}

async function main() {
	const visionModel = process.env.AI_VISION_MODEL_ID || '';
	if (!visionModel) {
		console.error('AI_VISION_MODEL_ID is not set — cannot write alt text. Exiting.');
		process.exit(1);
	}

	await mkdir(INBOX_ROOT, { recursive: true });
	const client = createClient();
	client
		.listModels()
		.then(() => console.log('LM Studio reachable.'))
		.catch(() => console.warn('⚠ LM Studio not reachable yet — will retry per dropped file.'));

	// chokidar v4 watches a directory path (no glob support). Recursive by default.
	let queue = Promise.resolve();
	const watcher = watch(INBOX_ROOT, {
		ignoreInitial: false,
		ignored: (p) => basename(p).startsWith('.'),
		awaitWriteFinish: { stabilityThreshold: 500, pollInterval: 100 },
	});
	watcher.on('add', (filePath) => {
		// Serialize processing — one file at a time.
		queue = queue
			.then(() => processFile(client, filePath))
			.catch((err) => console.error(`✗ ${filePath} — ${err.message}`));
	});

	console.log(`Watching ${INBOX_ROOT}/ for new images … (Ctrl-C to stop)`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
