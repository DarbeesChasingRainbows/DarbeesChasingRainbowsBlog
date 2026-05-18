import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, utimes } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const SCRIPT = new URL('./check-related-fresh.mjs', import.meta.url).pathname;

async function setup({ withRelated = true, mdxMtime, relatedMtime } = {}) {
	const dir = await mkdtemp(join(tmpdir(), 'fresh-'));
	await mkdir(join(dir, 'src/content/blog'), { recursive: true });
	await mkdir(join(dir, 'src/data'), { recursive: true });

	const post = join(dir, 'src/content/blog/hello.mdx');
	await writeFile(post, '---\ntitle: Hello\npubDate: 2026-01-01\n---\nbody', 'utf8');
	if (mdxMtime != null) await utimes(post, mdxMtime / 1000, mdxMtime / 1000);

	if (withRelated) {
		const related = join(dir, 'src/data/related-posts.json');
		await writeFile(related, '{}', 'utf8');
		if (relatedMtime != null) await utimes(related, relatedMtime / 1000, relatedMtime / 1000);
	}

	return dir;
}

function runScript(cwd, args = []) {
	return spawnSync(process.execPath, [SCRIPT, ...args], { cwd, encoding: 'utf8' });
}

test('all fresh: exits 0 with success message', async () => {
	const dir = await setup({ mdxMtime: 1_000_000, relatedMtime: 2_000_000 });
	const res = runScript(dir);
	assert.equal(res.status, 0);
	assert.match(res.stdout, /up-to-date/);
});

test('mdx newer than related-posts.json: warns, exits 0 (default)', async () => {
	const dir = await setup({ mdxMtime: 2_000_000, relatedMtime: 1_000_000 });
	const res = runScript(dir);
	assert.equal(res.status, 0);
	assert.match(res.stderr, /stale|out of date/i);
	assert.match(res.stderr, /blog\/hello/);
});

test('mdx newer with --strict: exits 1', async () => {
	const dir = await setup({ mdxMtime: 2_000_000, relatedMtime: 1_000_000 });
	const res = runScript(dir, ['--strict']);
	assert.equal(res.status, 1);
	assert.match(res.stderr, /stale/i);
});

test('missing related-posts.json: warns, exits 0 (default)', async () => {
	const dir = await setup({ withRelated: false });
	const res = runScript(dir);
	assert.equal(res.status, 0);
	assert.match(res.stderr, /missing/i);
});

test('missing related-posts.json with --strict: exits 1', async () => {
	const dir = await setup({ withRelated: false });
	const res = runScript(dir, ['--strict']);
	assert.equal(res.status, 1);
	assert.match(res.stderr, /missing/i);
});
