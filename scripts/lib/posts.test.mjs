import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { deriveId, stripMdx, embedText, contentHash, listPosts } from './posts.mjs';

test('deriveId returns the posix path under the collection root without .mdx', () => {
	assert.equal(deriveId('src/content/blog', 'src/content/blog/foo.mdx'), 'foo');
	assert.equal(deriveId('src/content/blog', 'src/content/blog/2024/bar.mdx'), '2024/bar');
});

test('stripMdx removes imports, JSX tags, and markdown punctuation', () => {
	const body = "import X from 'x';\n\n# Heading\n\n<Callout>hey</Callout>\n\nA [link](http://e.com).";
	const out = stripMdx(body);
	assert.equal(out.includes('import'), false);
	assert.equal(out.includes('<Callout>'), false);
	assert.equal(out.includes('#'), false);
	assert.equal(out.includes('Heading'), true);
	assert.equal(out.includes('link'), true);
});

test('embedText includes identity fields and stripped body', () => {
	const post = {
		frontmatter: { title: 'My Title', description: 'Desc', tags: ['a', 'b'], category: 'RV Life' },
		body: '# Hello world',
	};
	const text = embedText(post);
	assert.equal(text.includes('My Title'), true);
	assert.equal(text.includes('Tags: a, b'), true);
	assert.equal(text.includes('Category: RV Life'), true);
	assert.equal(text.includes('Hello world'), true);
});

test('contentHash is stable and changes with the body', () => {
	const a = { frontmatter: { title: 'T', description: 'D' }, body: 'one' };
	const b = { frontmatter: { title: 'T', description: 'D' }, body: 'one' };
	const c = { frontmatter: { title: 'T', description: 'D' }, body: 'two' };
	assert.equal(contentHash(a), contentHash(b));
	assert.notEqual(contentHash(a), contentHash(c));
});

test('listPosts walks collections, skips drafts and _templates', async () => {
	const root = await mkdtemp(join(tmpdir(), 'posts-'));
	try {
		await mkdir(join(root, 'blog'), { recursive: true });
		await mkdir(join(root, '_templates'), { recursive: true });
		await writeFile(join(root, 'blog', 'a.mdx'), '---\ntitle: A\ndraft: false\n---\nbody a');
		await writeFile(join(root, 'blog', 'b.mdx'), '---\ntitle: B\ndraft: true\n---\nbody b');
		await writeFile(join(root, '_templates', 't.mdx'), '---\ntitle: T\n---\ntemplate');

		const published = await listPosts({ contentRoot: root, collections: ['blog'] });
		assert.deepEqual(published.map((p) => p.id), ['a']);

		const all = await listPosts({ contentRoot: root, collections: ['blog'], includeDrafts: true });
		assert.deepEqual(all.map((p) => p.id).sort(), ['a', 'b']);
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});
