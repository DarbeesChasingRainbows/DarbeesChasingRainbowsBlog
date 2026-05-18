/**
 * Minimal HTTP wrapper for ArangoDB. Reads connection env vars at call time,
 * uses Basic auth, applies a per-call AbortSignal.timeout.
 */

export class ArangoError extends Error {
	constructor(message, { status, body } = {}) {
		super(message);
		this.name = 'ArangoError';
		this.status = status;
		this.body = body;
	}
}

function connInfo() {
	const url = (process.env.ARANGO_URL || 'http://localhost:8529').replace(/\/$/, '');
	const db = process.env.ARANGO_DATABASE || 'darbees_knowledge';
	const user = process.env.ARANGO_USER || 'root';
	const pass = process.env.ARANGO_PASSWORD || process.env.ARANGO_ROOT_PASSWORD || '';
	const auth = `Basic ${Buffer.from(`${user}:${pass}`).toString('base64')}`;
	return { url, db, auth };
}

export async function runAql(query, bindVars = {}, { timeoutMs = 30_000 } = {}) {
	const { url, db, auth } = connInfo();
	const endpoint = `${url}/_db/${encodeURIComponent(db)}/_api/cursor`;
	const signal = AbortSignal.timeout(timeoutMs);

	let response;
	try {
		response = await fetch(endpoint, {
			method: 'POST',
			headers: { 'content-type': 'application/json', Authorization: auth },
			body: JSON.stringify({ query, bindVars }),
			signal,
		});
	} catch (cause) {
		if (cause?.name === 'AbortError' || cause?.name === 'TimeoutError') {
			throw new ArangoError(`Arango timeout after ${timeoutMs}ms: ${endpoint}`);
		}
		throw new ArangoError(`Arango unreachable at ${endpoint}: ${cause.message}`);
	}

	const text = await response.text();
	let parsed;
	try {
		parsed = text ? JSON.parse(text) : null;
	} catch {
		parsed = text;
	}
	if (!response.ok) {
		throw new ArangoError(`Arango ${response.status}: ${text.slice(0, 200)}`, {
			status: response.status,
			body: parsed,
		});
	}
	return parsed?.result ?? [];
}
