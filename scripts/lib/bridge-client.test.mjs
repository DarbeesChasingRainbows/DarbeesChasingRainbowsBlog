import test from 'node:test';
import assert from 'node:assert/strict';
import { BridgeError, bridgePost } from './bridge-client.mjs';

test('bridgePost returns parsed JSON on 2xx', async () => {
	const originalFetch = globalThis.fetch;
	try {
		globalThis.fetch = async () =>
			new Response(JSON.stringify({ ok: true, n: 5 }), {
				status: 200,
				headers: { 'content-type': 'application/json' },
			});
		const result = await bridgePost('/x', { a: 1 }, { bridgeUrl: 'http://test' });
		assert.deepEqual(result, { ok: true, n: 5 });
	} finally {
		globalThis.fetch = originalFetch;
	}
});

test('bridgePost throws BridgeError on non-2xx with parsed body', async () => {
	const originalFetch = globalThis.fetch;
	try {
		globalThis.fetch = async () =>
			new Response(JSON.stringify({ error: 'invalid_request', details: 'bad' }), {
				status: 400,
				headers: { 'content-type': 'application/json' },
			});
		await assert.rejects(
			() => bridgePost('/x', { a: 1 }, { bridgeUrl: 'http://test' }),
			(err) => {
				assert.ok(err instanceof BridgeError);
				assert.equal(err.status, 400);
				assert.deepEqual(err.body, { error: 'invalid_request', details: 'bad' });
				return true;
			},
		);
	} finally {
		globalThis.fetch = originalFetch;
	}
});

test('bridgePost throws BridgeError when fetch itself throws (network)', async () => {
	const originalFetch = globalThis.fetch;
	try {
		globalThis.fetch = async () => {
			throw new TypeError('fetch failed');
		};
		await assert.rejects(
			() => bridgePost('/x', { a: 1 }, { bridgeUrl: 'http://test' }),
			(err) => {
				assert.ok(err instanceof BridgeError);
				assert.match(err.message, /bridge unreachable/);
				return true;
			},
		);
	} finally {
		globalThis.fetch = originalFetch;
	}
});

test('bridgePost strips trailing slash from bridgeUrl', async () => {
	const originalFetch = globalThis.fetch;
	let capturedUrl;
	try {
		globalThis.fetch = async (url) => {
			capturedUrl = url;
			return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } });
		};
		await bridgePost('/x', {}, { bridgeUrl: 'http://test/' });
		assert.equal(capturedUrl, 'http://test/x');
	} finally {
		globalThis.fetch = originalFetch;
	}
});

test('bridgePost aborts after timeoutMs and throws BridgeError mentioning timeout', async () => {
	const originalFetch = globalThis.fetch;
	try {
		globalThis.fetch = async (_url, init) =>
			new Promise((_, reject) => {
				init.signal.addEventListener('abort', () => {
					const err = new Error('aborted');
					err.name = 'AbortError';
					reject(err);
				});
			});
		await assert.rejects(
			() => bridgePost('/x', {}, { bridgeUrl: 'http://test', timeoutMs: 50 }),
			(err) => {
				assert.ok(err instanceof BridgeError);
				assert.match(err.message, /timeout|bridge timeout/i);
				return true;
			},
		);
	} finally {
		globalThis.fetch = originalFetch;
	}
});
