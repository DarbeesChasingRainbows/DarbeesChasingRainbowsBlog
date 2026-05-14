#!/usr/bin/env node
/**
 * #6 related posts — embed every published post, compute cosine similarity,
 * write src/data/related-posts.json (consumed at Astro build time).
 *
 * This file exports its pure helpers (for tests) and runs the rebuild when
 * invoked directly. Orchestration lives below the helpers.
 */

/** Cosine similarity of two equal-length numeric vectors. Returns 0 for a zero vector. */
export function cosineSimilarity(a, b) {
	let dot = 0,
		na = 0,
		nb = 0;
	for (let i = 0; i < a.length; i++) {
		dot += a[i] * b[i];
		na += a[i] * a[i];
		nb += b[i] * b[i];
	}
	if (na === 0 || nb === 0) return 0;
	return dot / (Math.sqrt(na) * Math.sqrt(nb));
}

/** Cache key: content hash + embedding model id, so a model swap invalidates the cache. */
export function cacheKey(contentHashValue, embeddingModelId) {
	return `${contentHashValue}:${embeddingModelId}`;
}

/**
 * For one post's vector, the top `limit` of `others` with score >= floor, highest first.
 * `others` is [{ collection, id, vector }] — caller has already excluded self.
 */
export function topRelated(vector, others, { limit = 3, floor = 0.6 } = {}) {
	return others
		.map((o) => ({ id: o.id, collection: o.collection, score: cosineSimilarity(vector, o.vector) }))
		.filter((o) => o.score >= floor)
		.sort((a, b) => b.score - a.score)
		.slice(0, limit);
}

/**
 * Build the full related-posts map from [{ collection, id, vector }].
 * Key is `${collection}/${id}`; each post's own entry is excluded from its list.
 */
export function buildRelatedMap(posts, opts) {
	const map = {};
	for (const post of posts) {
		const others = posts.filter((p) => !(p.collection === post.collection && p.id === post.id));
		map[`${post.collection}/${post.id}`] = topRelated(post.vector, others, opts);
	}
	return map;
}

// ---------------------------------------------------------------------------
// Orchestration — runs only when this file is invoked directly.
// ---------------------------------------------------------------------------
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { createClient } from './lib/lmstudio.mjs';
import { listPosts, embedText, contentHash, PRIMARY_COLLECTIONS } from './lib/posts.mjs';

const DATA_DIR = 'src/data';
const OUT_PATH = `${DATA_DIR}/related-posts.json`;
const CACHE_PATH = `${DATA_DIR}/related-posts.cache.json`;

async function readJson(path, fallback) {
	try {
		return JSON.parse(await readFile(path, 'utf8'));
	} catch {
		return fallback;
	}
}

async function main() {
	const embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || '';
	if (!embeddingModel) {
		console.error('AI_EMBEDDING_MODEL_ID is not set — cannot rebuild related posts.');
		process.exit(1);
	}

	const client = createClient();
	const posts = await listPosts({ collections: PRIMARY_COLLECTIONS, includeDrafts: false });
	const cache = await readJson(CACHE_PATH, {});
	const nextCache = {};
	let embedded = 0,
		fromCache = 0;

	const withVectors = [];
	for (const post of posts) {
		const key = cacheKey(contentHash(post), embeddingModel);
		let vector = cache[key];
		if (vector) {
			fromCache++;
		} else {
			vector = await client.embed(embedText(post));
			embedded++;
		}
		nextCache[key] = vector;
		withVectors.push({ collection: post.collection, id: post.id, vector });
	}

	const map = buildRelatedMap(withVectors, { limit: 3, floor: 0.6 });
	const orphans = Object.values(map).filter((r) => r.length === 0).length;

	await mkdir(DATA_DIR, { recursive: true });
	await writeFile(OUT_PATH, `${JSON.stringify(map, null, '\t')}\n`, 'utf8');
	await writeFile(CACHE_PATH, `${JSON.stringify(nextCache, null, '\t')}\n`, 'utf8');

	console.log(`${embedded} embedded, ${fromCache} from cache, ${orphans} posts with 0 relations`);
	console.log(`Wrote ${OUT_PATH}`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
