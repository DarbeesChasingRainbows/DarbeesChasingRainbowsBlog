import test from 'node:test';
import assert from 'node:assert/strict';
import { ArangoError, runAql } from './arango-client.mjs';

function withFetch(stub, fn) {
	const original = globalThis.fetch;
	globalThis.fetch = stub;
	return Promise.resolve(fn()).finally(() => {
		globalThis.fetch = original;
	});
}

function jsonResponse(body, { status = 200 } = {}) {
	return new Response(JSON.stringify(body), {
		status,
		headers: { 'content-type': 'application/json' },
	});
}

test('runAql returns the result array on 2xx', async () => {
	await withFetch(
		async () => jsonResponse({ result: [{ a: 1 }, { a: 2 }], hasMore: false }),
		async () => {
			const rows = await runAql('FOR x IN c RETURN x');
			assert.deepEqual(rows, [{ a: 1 }, { a: 2 }]);
		},
	);
});

test('runAql throws ArangoError with status and parsed body on 4xx', async () => {
	await withFetch(
		async () => jsonResponse({ errorMessage: 'bad query', code: 400 }, { status: 400 }),
		async () => {
			await assert.rejects(
				() => runAql('NOT AQL'),
				(err) => {
					assert.ok(err instanceof ArangoError);
					assert.equal(err.status, 400);
					assert.equal(err.body.errorMessage, 'bad query');
					return true;
				},
			);
		},
	);
});

test('runAql 404 surfaces "database not found" path', async () => {
	await withFetch(
		async () => jsonResponse({ errorMessage: 'database not found' }, { status: 404 }),
		async () => {
			await assert.rejects(
				() => runAql('FOR x IN c RETURN x'),
				(err) => {
					assert.equal(err.status, 404);
					return true;
				},
			);
		},
	);
});

test('runAql wraps fetch network failures in ArangoError', async () => {
	await withFetch(
		async () => {
			throw new TypeError('fetch failed');
		},
		async () => {
			await assert.rejects(
				() => runAql('FOR x IN c RETURN x'),
				(err) => {
					assert.ok(err instanceof ArangoError);
					assert.match(err.message, /unreachable/);
					return true;
				},
			);
		},
	);
});

test('runAql sends Basic auth from env vars', async () => {
	process.env.ARANGO_USER = 'root';
	process.env.ARANGO_PASSWORD = 's3cret';
	let capturedAuth;
	await withFetch(
		async (_url, init) => {
			capturedAuth = init.headers.Authorization;
			return jsonResponse({ result: [] });
		},
		async () => {
			await runAql('FOR x IN c RETURN x');
		},
	);
	const expected = `Basic ${Buffer.from('root:s3cret').toString('base64')}`;
	assert.equal(capturedAuth, expected);
});

test('runAql aborts after timeoutMs and throws ArangoError mentioning timeout', async () => {
	await withFetch(
		async (_url, init) => {
			return new Promise((_, reject) => {
				init.signal.addEventListener('abort', () => {
					const err = new Error('aborted');
					err.name = 'AbortError';
					reject(err);
				});
			});
		},
		async () => {
			await assert.rejects(
				() => runAql('FOR x IN c RETURN x', {}, { timeoutMs: 50 }),
				(err) => {
					assert.ok(err instanceof ArangoError);
					assert.match(err.message, /timeout|Arango timeout/i);
					return true;
				},
			);
		},
	);
});
