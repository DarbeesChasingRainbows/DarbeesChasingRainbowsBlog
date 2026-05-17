import test from 'node:test';
import assert from 'node:assert/strict';
import { parseArgs } from './rag-reindex.mjs';

test('parseArgs defaults: not force, all collections, no bridge override', () => {
	const args = parseArgs(['node', 'rag-reindex.mjs']);
	assert.equal(args.force, false);
	assert.deepEqual(args.collections, ['blog', 'projects', 'field-notes', 'books']);
	assert.equal(args.bridgeUrl, undefined);
});

test('parseArgs --force sets the force flag', () => {
	const args = parseArgs(['node', 'rag-reindex.mjs', '--force']);
	assert.equal(args.force, true);
});

test('parseArgs --collections parses comma-separated list', () => {
	const args = parseArgs(['node', 'rag-reindex.mjs', '--collections', 'blog,projects']);
	assert.deepEqual(args.collections, ['blog', 'projects']);
});

test('parseArgs --collections trims whitespace', () => {
	const args = parseArgs(['node', 'rag-reindex.mjs', '--collections', 'blog , projects']);
	assert.deepEqual(args.collections, ['blog', 'projects']);
});

test('parseArgs --bridge-url captures the next arg', () => {
	const args = parseArgs(['node', 'rag-reindex.mjs', '--bridge-url', 'http://example:5000']);
	assert.equal(args.bridgeUrl, 'http://example:5000');
});

test('parseArgs combines flags', () => {
	const args = parseArgs([
		'node',
		'rag-reindex.mjs',
		'--force',
		'--collections',
		'blog',
		'--bridge-url',
		'http://x',
	]);
	assert.equal(args.force, true);
	assert.deepEqual(args.collections, ['blog']);
	assert.equal(args.bridgeUrl, 'http://x');
});
