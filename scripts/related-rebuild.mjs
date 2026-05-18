#!/usr/bin/env node
/**
 * Related posts (#6) — read body vectors from Arango (memory_posts), compute
 * pairwise cosine similarity, write src/data/related-posts.json (consumed at
 * Astro build time). Arango is the single source of truth for vectors; this
 * script does not embed.
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

/**
 * For one post's vector, the top `limit` of `others` with score >= floor, highest first.
 * `others` is [{ collection, id, vector }] — caller has already excluded self.
 */
export function topRelated(vector, others, { limit = 3, floor = 0.5 } = {}) {
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
import { writeFile, mkdir } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { ArangoError, runAql } from './lib/arango-client.mjs';

const DATA_DIR = 'src/data';
const OUT_PATH = `${DATA_DIR}/related-posts.json`;

async function main() {
	const aql = `
        FOR doc IN memory_posts
          FILTER doc.tenant_id == "public"
          FILTER doc.status == "ready"
          FILTER doc.vector_kind == "body"
          RETURN { collection: doc.collection, id: doc.slug, vector: doc.embedding }
    `;

	let rows;
	try {
		rows = await runAql(aql);
	} catch (err) {
		if (err instanceof ArangoError) {
			console.error(`Arango error: ${err.message}`);
			if (err.status === 404) {
				console.error(
					'Hint: database `darbees_knowledge` may not exist yet. ' +
						'Run `make up` then `npm run rag:reindex` to bootstrap it.',
				);
			}
		} else {
			console.error(err.stack || err.message);
		}
		process.exit(1);
	}

	if (rows.length === 0) {
		console.error('No ready vectors in memory_posts. Run `npm run rag:reindex` first.');
		process.exit(1);
	}

	const dims = new Set(rows.map((r) => r.vector.length));
	if (dims.size !== 1) {
		console.error(`Inconsistent vector dimensions in memory_posts: ${[...dims].join(', ')}`);
		console.error('This indicates a partial migration. Run `npm run rag:reindex -- --force` to repair.');
		process.exit(1);
	}

	const floor = Number(process.env.RELATED_FLOOR ?? 0.5);
	const map = buildRelatedMap(rows, { limit: 3, floor });
	const orphans = Object.values(map).filter((r) => r.length === 0).length;

	await mkdir(DATA_DIR, { recursive: true });
	await writeFile(OUT_PATH, `${JSON.stringify(map, null, '\t')}\n`, 'utf8');

	console.log(`${rows.length} posts indexed, ${orphans} with 0 relations (floor=${floor})`);
	console.log(`Wrote ${OUT_PATH}`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
