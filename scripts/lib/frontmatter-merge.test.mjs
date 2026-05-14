import test from 'node:test';
import assert from 'node:assert/strict';
import matter from 'gray-matter';
import { isEmpty, mergeFrontmatter, serialize } from './frontmatter-merge.mjs';

test('isEmpty treats undefined/null/blank/empty-array as empty', () => {
	assert.equal(isEmpty(undefined), true);
	assert.equal(isEmpty(null), true);
	assert.equal(isEmpty(''), true);
	assert.equal(isEmpty('   '), true);
	assert.equal(isEmpty([]), true);
	assert.equal(isEmpty('x'), false);
	assert.equal(isEmpty(['a']), false);
	assert.equal(isEmpty(0), false);
	assert.equal(isEmpty(false), false);
});

test('mergeFrontmatter fills only empty keys by default', () => {
	const existing = { title: 'T', aiSummary: '' };
	const generated = { aiSummary: 'S', title: 'NEW' };
	const { merged, changedKeys } = mergeFrontmatter(existing, generated);
	assert.equal(merged.aiSummary, 'S');
	assert.equal(merged.title, 'T');
	assert.deepEqual(changedKeys, ['aiSummary']);
});

test('mergeFrontmatter with force overwrites existing keys', () => {
	const existing = { title: 'T', aiSummary: 'old' };
	const generated = { aiSummary: 'S', title: 'NEW' };
	const { merged, changedKeys } = mergeFrontmatter(existing, generated, { force: true });
	assert.equal(merged.title, 'NEW');
	assert.equal(merged.aiSummary, 'S');
	assert.deepEqual(changedKeys.sort(), ['aiSummary', 'title']);
});

test('mergeFrontmatter preserves existing key order and appends new keys', () => {
	const existing = { title: 'T', description: 'D', draft: true };
	const generated = { aiSummary: 'S', keyTakeaways: ['a'] };
	const { merged } = mergeFrontmatter(existing, generated);
	assert.deepEqual(Object.keys(merged), [
		'title',
		'description',
		'draft',
		'aiSummary',
		'keyTakeaways',
	]);
});

test('serialize round-trips body unchanged and writes frontmatter', () => {
	const out = serialize({ title: 'T' }, 'body text here');
	const parsed = matter(out);
	assert.equal(parsed.data.title, 'T');
	assert.equal(parsed.content.trim(), 'body text here');
});
