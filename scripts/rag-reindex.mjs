#!/usr/bin/env node
/**
 * Walk src/content/**\/*.mdx, POST structured posts to the dais-bridge
 * reindex endpoint, print a human-readable summary. Mirrors the pattern
 * of scripts/related-rebuild.mjs.
 */

import { listPosts, stripMdx, ALL_COLLECTIONS } from './lib/posts.mjs';
import { bridgePost, BridgeError } from './lib/bridge-client.mjs';

function parseArgs(argv) {
	const args = { force: false, collections: ALL_COLLECTIONS, bridgeUrl: undefined };
	for (let i = 2; i < argv.length; i++) {
		const a = argv[i];
		if (a === '--force') args.force = true;
		else if (a === '--collections') args.collections = argv[++i].split(',').map((s) => s.trim());
		else if (a === '--bridge-url') args.bridgeUrl = argv[++i];
		else if (a === '-h' || a === '--help') {
			console.log(
				'usage: rag-reindex [--force] [--collections blog,projects] [--bridge-url http://localhost:5000]'
			);
			process.exit(0);
		}
	}
	return args;
}

async function main() {
	const args = parseArgs(process.argv);

	const posts = await listPosts({ collections: args.collections });
	console.log(`Found ${posts.length} posts across ${args.collections.join(', ')}.`);

	if (posts.length === 0) {
		console.error('No posts found — refusing to reindex (would wipe the memory_posts collection).');
		console.error(`Working dir: ${process.cwd()}`);
		console.error(`Collections searched: ${args.collections.join(', ')}`);
		process.exit(1);
	}

	const payload = {
		force: args.force,
		posts: posts.map((p) => ({
			collection: p.collection,
			slug: p.id,
			frontmatter: {
				title: p.frontmatter.title ?? '',
				description: p.frontmatter.description ?? '',
				pubDate: p.frontmatter.pubDate ?? null,
				category: p.frontmatter.category ?? null,
				tags: p.frontmatter.tags ?? [],
				aiSummary: p.frontmatter.aiSummary ?? null,
				keyTakeaways: p.frontmatter.keyTakeaways ?? [],
				faq: (p.frontmatter.faq ?? []).map((f) => ({ question: f.question, answer: f.answer })),
				entityMentions: p.frontmatter.entityMentions ?? [],
			},
			body: stripMdx(p.body),
		})),
	};

	try {
		const result = await bridgePost('/api/admin/reindex-posts', payload, {
			bridgeUrl: args.bridgeUrl,
		});

		for (const r of result.posts) {
			const symbol = r.summary === 'failed' || r.body === 'failed' ? '✗' : '✓';
			console.log(`${symbol} ${r.collection}/${r.slug}`);
			console.log(`  summary: ${r.summary}, body: ${r.body}`);
			if (r.failureReason) console.log(`  ! ${r.failureReason}`);
		}

		const failedCount = result.posts.filter((p) => p.summary === 'failed' || p.body === 'failed').length;

		console.log('');
		console.log(
			`${result.scanned} posts: ${result.fromCache} cached, ${result.embedded} embedded, ${failedCount} failed`
		);
		console.log(`Deleted ${result.deletedStale} stale doc(s).`);
		console.log(`Duration: ${(result.durationMs / 1000).toFixed(1)}s`);

		process.exit(failedCount > 0 ? 1 : 0);
	} catch (err) {
		if (err instanceof BridgeError) {
			console.error(`bridge error (${err.status ?? 'no status'}): ${err.message}`);
			if (err.body) console.error(JSON.stringify(err.body, null, 2));
		} else {
			console.error(err.stack || err.message);
		}
		process.exit(1);
	}
}

main();
