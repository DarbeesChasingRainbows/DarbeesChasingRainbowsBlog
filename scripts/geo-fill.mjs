#!/usr/bin/env node
/**
 * #5 GEO auto-fill — populate aiSummary / keyTakeaways / faq on a post.
 *
 *   npm run geo:fill -- src/content/blog/<slug>.mdx     one post (any draft state)
 *   npm run geo:fill:all                                every published post
 *   ... -- --force                                      overwrite existing fields
 *
 * Fills only empty fields by default. Never touches the body. Never commits.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import matter from 'gray-matter';
import { createClient } from './lib/lmstudio.mjs';
import { listPosts, stripMdx, ALL_COLLECTIONS } from './lib/posts.mjs';
import { mergeFrontmatter, serialize } from './lib/frontmatter-merge.mjs';

const GREEN = '\x1b[32m';
const DIM = '\x1b[2m';
const RESET = '\x1b[0m';

const GEO_SCHEMA = {
	name: 'geo_fields',
	required: ['aiSummary', 'keyTakeaways', 'faq'],
	shape: {
		type: 'object',
		properties: {
			aiSummary: { type: 'string' },
			keyTakeaways: { type: 'array', items: { type: 'string' }, minItems: 3, maxItems: 6 },
			faq: {
				type: 'array',
				maxItems: 4,
				items: {
					type: 'object',
					properties: { question: { type: 'string' }, answer: { type: 'string' } },
					required: ['question', 'answer'],
				},
			},
		},
		required: ['aiSummary', 'keyTakeaways', 'faq'],
	},
};

export function buildGeoMessages(post) {
	const { title = '', description = '' } = post.frontmatter;
	return [
		{
			role: 'system',
			content:
				'You write GEO (Generative Engine Optimization) metadata for a family blog. ' +
				'Write in third person. aiSummary: 1-2 citation-ready sentences naming the entity. ' +
				'keyTakeaways: 3-6 short scannable bullets. faq: 0-4 genuine reader questions with ' +
				'concrete answers. Return JSON only.',
		},
		{
			role: 'user',
			content: `Title: ${title}\nDescription: ${description}\n\nBody:\n${stripMdx(post.body)}`,
		},
	];
}

function parseArgs(argv) {
	const args = argv.slice(2);
	return {
		force: args.includes('--force'),
		all: args.includes('--all'),
		path: args.find((a) => !a.startsWith('--')),
	};
}

function printDiff(path, changedKeys, merged) {
	console.log(`${GREEN}✓${RESET} ${path}`);
	for (const key of changedKeys) {
		const value = JSON.stringify(merged[key]);
		const shown = value.length > 120 ? `${value.slice(0, 117)}...` : value;
		console.log(`  ${GREEN}+ ${key}${RESET} ${DIM}${shown}${RESET}`);
	}
}

async function fillOne(client, post, force) {
	const generated = await client.chatJson(buildGeoMessages(post), GEO_SCHEMA);
	const raw = await readFile(post.path, 'utf8');
	const { data: existing, content: body } = matter(raw);
	const { merged, changedKeys } = mergeFrontmatter(existing, generated, { force });
	if (changedKeys.length === 0) return { status: 'skipped' };
	await writeFile(post.path, serialize(merged, body), 'utf8');
	return { status: 'filled', changedKeys, merged };
}

async function main() {
	const { force, all, path } = parseArgs(process.argv);
	if (!all && !path) {
		console.error('Usage: npm run geo:fill -- <path-to-post.mdx> [-- --force]');
		console.error('       npm run geo:fill:all [-- --force]');
		process.exit(1);
	}

	const client = createClient();
	let posts;
	if (all) {
		posts = await listPosts({ collections: ALL_COLLECTIONS, includeDrafts: false });
	} else {
		const raw = await readFile(path, 'utf8');
		const { data: frontmatter, content: body } = matter(raw);
		posts = [{ collection: '', id: path, path, frontmatter, body }];
	}

	let filled = 0,
		skipped = 0,
		failed = 0;
	for (const post of posts) {
		try {
			const result = await fillOne(client, post, force);
			if (result.status === 'filled') {
				filled++;
				printDiff(post.path, result.changedKeys, result.merged);
			} else {
				skipped++;
				console.log(`${DIM}· ${post.path} — already populated${RESET}`);
			}
		} catch (err) {
			failed++;
			console.error(`✗ ${post.path} — ${err.message}`);
		}
	}
	console.log(`\n${filled} filled, ${skipped} skipped, ${failed} failed`);
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
	main();
}
