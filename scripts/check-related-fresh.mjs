#!/usr/bin/env node
/**
 * Compare mtimes of published MDX files against src/data/related-posts.json.
 * Warns by default, exits 1 with --strict. Used as the npm `prebuild` hook.
 *
 * Paths are resolved relative to process.cwd(); run from the repo root.
 */
import { stat } from 'node:fs/promises';
import { listPosts, PRIMARY_COLLECTIONS } from './lib/posts.mjs';

const RELATED_POSTS = 'src/data/related-posts.json';
const MAX_SLUGS_SHOWN = 5;

async function main() {
	const strict = process.argv.includes('--strict');

	let relatedMtime;
	try {
		relatedMtime = (await stat(RELATED_POSTS)).mtimeMs;
	} catch (err) {
		if (err.code !== 'ENOENT') throw err;
		console.warn(`⚠ ${RELATED_POSTS} is missing. Run \`npm run rag:rebuild-all\`.`);
		process.exit(strict ? 1 : 0);
	}

	const posts = await listPosts({ collections: PRIMARY_COLLECTIONS });
	const stale = [];
	for (const p of posts) {
		const m = (await stat(p.path)).mtimeMs;
		if (m > relatedMtime) stale.push(`${p.collection}/${p.id}`);
	}

	if (stale.length === 0) {
		console.log(`✓ related-posts.json is up-to-date (${posts.length} posts checked)`);
		return;
	}

	console.warn(`⚠ related-posts.json is stale for ${stale.length} post(s):`);
	for (const s of stale.slice(0, MAX_SLUGS_SHOWN)) console.warn(`    ${s}`);
	if (stale.length > MAX_SLUGS_SHOWN) {
		console.warn(`    ... and ${stale.length - MAX_SLUGS_SHOWN} more`);
	}
	console.warn('  Run `npm run rag:rebuild-all` to refresh.');
	process.exit(strict ? 1 : 0);
}

main().catch((err) => {
	console.error(err.stack || err.message);
	process.exit(1);
});
