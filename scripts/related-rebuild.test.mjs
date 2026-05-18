import test from 'node:test';
import assert from 'node:assert/strict';
import { cosineSimilarity, topRelated, buildRelatedMap } from './related-rebuild.mjs';

test('cosineSimilarity: identical=1, orthogonal=0, zero-vector=0', () => {
	assert.equal(cosineSimilarity([1, 0], [1, 0]), 1);
	assert.equal(cosineSimilarity([1, 0], [0, 1]), 0);
	assert.equal(cosineSimilarity([0, 0], [1, 1]), 0);
	assert.ok(Math.abs(cosineSimilarity([1, 0], [1, 1]) - 0.7071) < 0.001);
});

test('topRelated applies the floor, sorts desc, and caps at the limit', () => {
	const others = [
		{ collection: 'blog', id: 'same', vector: [1, 0] }, // score 1
		{ collection: 'projects', id: 'half', vector: [1, 1] }, // score ~0.707
		{ collection: 'blog', id: 'ortho', vector: [0, 1] }, // score 0 — below floor
	];
	const result = topRelated([1, 0], others, { limit: 3, floor: 0.6 });
	assert.deepEqual(
		result.map((r) => r.id),
		['same', 'half'],
	);
	assert.ok(result[0].score >= result[1].score);
});

test('topRelated honours the limit', () => {
	const others = [
		{ collection: 'blog', id: 'a', vector: [1, 0] },
		{ collection: 'blog', id: 'b', vector: [1, 0] },
		{ collection: 'blog', id: 'c', vector: [1, 0] },
	];
	assert.equal(topRelated([1, 0], others, { limit: 2, floor: 0.6 }).length, 2);
});

test('buildRelatedMap uses composite collection/id keys and excludes self', () => {
	const posts = [
		{ collection: 'blog', id: 'a', vector: [1, 0] },
		{ collection: 'projects', id: 'a', vector: [1, 0] },
	];
	const map = buildRelatedMap(posts, { limit: 3, floor: 0.6 });
	assert.deepEqual(Object.keys(map).sort(), ['blog/a', 'projects/a']);
	assert.deepEqual(
		map['blog/a'].map((r) => `${r.collection}/${r.id}`),
		['projects/a'],
	);
});
