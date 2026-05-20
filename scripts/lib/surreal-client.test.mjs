import test from 'node:test';
import assert from 'node:assert/strict';
import { SurrealError, runSurrealQL } from './surreal-client.mjs';

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

test('runSurrealQL returns the result array of the first statement on 2xx', async () => {
	await withFetch(
		async () => jsonResponse([{ status: 'OK', result: [{ a: 1 }, { a: 2 }], time: '1ms' }]),
		async () => {
			const rows = await runSurrealQL('SELECT * FROM c;');
			assert.deepEqual(rows, [{ a: 1 }, { a: 2 }]);
		},
	);
});

test('runSurrealQL sends the raw query string as the request body', async () => {
	let capturedBody;
	await withFetch(
		async (_url, init) => {
			capturedBody = init.body;
			return jsonResponse([{ status: 'OK', result: [], time: '1ms' }]);
		},
		async () => {
			await runSurrealQL('SELECT * FROM memory_posts;');
		},
	);
	assert.equal(capturedBody, 'SELECT * FROM memory_posts;');
});

test('runSurrealQL throws SurrealError with status and parsed body on non-2xx', async () => {
	await withFetch(
		async () => jsonResponse({ code: 400, information: 'bad query' }, { status: 400 }),
		async () => {
			await assert.rejects(
				() => runSurrealQL('NOT SURREALQL'),
				(err) => {
					assert.ok(err instanceof SurrealError);
					assert.equal(err.status, 400);
					assert.equal(err.body.information, 'bad query');
					return true;
				},
			);
		},
	);
});

test('runSurrealQL wraps fetch network failures in SurrealError', async () => {
	await withFetch(
		async () => {
			throw new TypeError('fetch failed');
		},
		async () => {
			await assert.rejects(
				() => runSurrealQL('SELECT * FROM c;'),
				(err) => {
					assert.ok(err instanceof SurrealError);
					assert.match(err.message, /unreachable/);
					return true;
				},
			);
		},
	);
});

test('runSurrealQL sends Basic auth + surreal-ns/surreal-db headers from env vars', async () => {
	const prev = {
		user: process.env.SURREAL_USER,
		pass: process.env.SURREAL_PASS,
		ns: process.env.SURREAL_NS,
		db: process.env.SURREAL_DB,
	};
	process.env.SURREAL_USER = 'root';
	process.env.SURREAL_PASS = 's3cret';
	process.env.SURREAL_NS = 'darbees';
	process.env.SURREAL_DB = 'memory';
	try {
		let headers;
		await withFetch(
			async (_url, init) => {
				headers = init.headers;
				return jsonResponse([{ status: 'OK', result: [], time: '1ms' }]);
			},
			async () => {
				await runSurrealQL('SELECT * FROM c;');
			},
		);
		assert.equal(headers.Authorization, `Basic ${Buffer.from('root:s3cret').toString('base64')}`);
		assert.equal(headers['surreal-ns'], 'darbees');
		assert.equal(headers['surreal-db'], 'memory');
	} finally {
		for (const [k, envKey] of [
			['user', 'SURREAL_USER'],
			['pass', 'SURREAL_PASS'],
			['ns', 'SURREAL_NS'],
			['db', 'SURREAL_DB'],
		]) {
			if (prev[k] === undefined) delete process.env[envKey];
			else process.env[envKey] = prev[k];
		}
	}
});

test('runSurrealQL passes bind vars as JSON-encoded URL query parameters', async () => {
	let capturedUrl;
	await withFetch(
		async (url) => {
			capturedUrl = url;
			return jsonResponse([{ status: 'OK', result: [], time: '1ms' }]);
		},
		async () => {
			await runSurrealQL('SELECT * FROM c WHERE k = $k;', { k: 'value' });
		},
	);
	assert.match(capturedUrl, /\/sql\?/);
	assert.match(capturedUrl, /k=%22value%22/);
});

test('runSurrealQL aborts after timeoutMs and throws SurrealError mentioning timeout', async () => {
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
				() => runSurrealQL('SELECT * FROM c;', {}, { timeoutMs: 50 }),
				(err) => {
					assert.ok(err instanceof SurrealError);
					assert.match(err.message, /timeout/i);
					return true;
				},
			);
		},
	);
});
