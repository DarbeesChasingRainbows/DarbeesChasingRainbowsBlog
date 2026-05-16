import test from 'node:test';
import assert from 'node:assert/strict';
import { createClient } from './lmstudio.mjs';

/** Build a fake fetch that records calls and returns a canned JSON response. */
function fakeFetch(responseBody, { ok = true, status = 200 } = {}) {
	const calls = [];
	const fn = async (url, init) => {
		calls.push({ url, init });
		return {
			ok,
			status,
			json: async () => responseBody,
			text: async () => JSON.stringify(responseBody),
		};
	};
	fn.calls = calls;
	return fn;
}

const SCHEMA = {
	name: 'demo',
	required: ['value'],
	shape: { type: 'object', properties: { value: { type: 'string' } }, required: ['value'] },
};

test('chatJson sends a json_schema response_format and parses the content string', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"value":"hi"}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	const result = await client.chatJson([{ role: 'user', content: 'q' }], SCHEMA);
	assert.deepEqual(result, { value: 'hi' });

	const body = JSON.parse(fetch.calls[0].init.body);
	assert.equal(fetch.calls[0].url, 'http://lm/v1/chat/completions');
	assert.equal(body.response_format.type, 'json_schema');
	assert.equal(body.response_format.json_schema.name, 'demo');
	assert.ok(body.response_format.json_schema.schema);
});

test('chatJson throws when a required key is missing', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"other":1}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	await assert.rejects(() => client.chatJson([], SCHEMA), /missing key "value"/);
});

test('vision builds a multimodal content array and requires a vision model', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: '{"value":"alt"}' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', visionModel: 'v1' });
	const result = await client.vision(Buffer.from('img'), 'describe', SCHEMA);
	assert.deepEqual(result, { value: 'alt' });

	const body = JSON.parse(fetch.calls[0].init.body);
	const parts = body.messages[0].content;
	assert.equal(parts[0].type, 'text');
	assert.equal(parts[1].type, 'image_url');
	assert.ok(parts[1].image_url.url.startsWith('data:image/jpeg;base64,'));

	const noVision = createClient({ fetch, baseUrl: 'http://lm/v1', visionModel: '' });
	await assert.rejects(() => noVision.vision(Buffer.from('x'), 'p', SCHEMA), /AI_VISION_MODEL_ID/);
});

test('embed always sends an array input and unwraps single strings', async () => {
	const fetch = fakeFetch({ data: [{ embedding: [1, 2, 3] }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', embeddingModel: 'e1' });
	const single = await client.embed('hello');
	assert.deepEqual(single, [1, 2, 3]);
	assert.ok(Array.isArray(JSON.parse(fetch.calls[0].init.body).input));
});

test('a non-ok response throws', async () => {
	const fetch = fakeFetch({ error: 'boom' }, { ok: false, status: 500 });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', chatModel: 'm1' });
	await assert.rejects(() => client.chat([{ role: 'user', content: 'q' }]), /500/);
});

test('per-task base URLs route chat / embedding / vision to different servers', async () => {
	const fetch = fakeFetch({
		choices: [{ message: { content: '{"value":"hi"}' } }],
		data: [{ embedding: [0] }],
	});
	const client = createClient({
		fetch,
		baseUrl: 'http://default/v1',
		chatBaseUrl: 'http://chat/v1',
		embeddingBaseUrl: 'http://embed/v1',
		visionBaseUrl: 'http://vision/v1',
		chatModel: 'c',
		embeddingModel: 'e',
		visionModel: 'v',
	});

	await client.chat([{ role: 'user', content: 'q' }]);
	await client.embed('hi');
	await client.vision(Buffer.from('img'), 'p', SCHEMA);

	assert.equal(fetch.calls[0].url, 'http://chat/v1/chat/completions');
	assert.equal(fetch.calls[1].url, 'http://embed/v1/embeddings');
	assert.equal(fetch.calls[2].url, 'http://vision/v1/chat/completions');
});

test('Authorization header is omitted when apiKey is empty (llama.cpp)', async () => {
	const fetch = fakeFetch({ choices: [{ message: { content: 'ok' } }] });
	const client = createClient({ fetch, baseUrl: 'http://lm/v1', apiKey: '', chatModel: 'm' });
	await client.chat([{ role: 'user', content: 'q' }]);
	assert.equal(fetch.calls[0].init.headers.Authorization, undefined);
});
