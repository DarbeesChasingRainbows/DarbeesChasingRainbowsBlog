import test from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import {
	deriveCollectionSlug,
	sanitizeName,
	classifyFormat,
	resolveOutputPath,
	buildMarkdownSnippet,
} from './image-watcher.mjs';

test('deriveCollectionSlug parses inbox/<collection>/<slug>/<file>', () => {
	const root = 'obsidian-templates/inbox';
	assert.deepEqual(deriveCollectionSlug(root, join(root, 'blog', 'my-post', 'a.heic')), {
		collection: 'blog',
		slug: 'my-post',
		filename: 'a.heic',
	});
});

test('deriveCollectionSlug rejects bad depth and unknown collections', () => {
	const root = 'obsidian-templates/inbox';
	assert.equal(deriveCollectionSlug(root, join(root, 'blog', 'a.heic')), null);
	assert.equal(deriveCollectionSlug(root, join(root, 'nope', 'slug', 'a.heic')), null);
});

test('sanitizeName lowercases, hyphenates, strips junk, and keeps the extension', () => {
	assert.deepEqual(sanitizeName('My Photo 1.HEIC'), { base: 'my-photo-1', ext: '.heic' });
	assert.deepEqual(sanitizeName('weird!!name.JPG'), { base: 'weirdname', ext: '.jpg' });
	assert.deepEqual(sanitizeName('***.png'), { base: 'image', ext: '.png' });
});

test('classifyFormat distinguishes heic / web / unsupported', () => {
	assert.equal(classifyFormat('.heic'), 'heic');
	assert.equal(classifyFormat('.HEIF'), 'heic');
	assert.equal(classifyFormat('.jpg'), 'web');
	assert.equal(classifyFormat('.png'), 'web');
	assert.equal(classifyFormat('.webp'), 'web');
	assert.equal(classifyFormat('.gif'), 'unsupported');
});

test('resolveOutputPath suffixes -2, -3 on collision', () => {
	const taken = new Set(['out/a.jpg', 'out/a-2.jpg']);
	const exists = (p) => taken.has(p);
	assert.equal(resolveOutputPath('out', 'b', '.jpg', exists), join('out', 'b.jpg'));
	assert.equal(resolveOutputPath('out', 'a', '.jpg', exists), join('out', 'a-3.jpg'));
});

test('buildMarkdownSnippet produces a relative assets image tag', () => {
	assert.equal(
		buildMarkdownSnippet('blog', 'my-post', 'photo.jpg', 'A sunset'),
		'![A sunset](../../assets/blog/my-post/photo.jpg)',
	);
});
