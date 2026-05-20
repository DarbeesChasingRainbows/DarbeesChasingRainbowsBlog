/**
 * Minimal HTTP wrapper for SurrealDB. Reads connection env vars at call time,
 * uses Basic auth, applies a per-call AbortSignal.timeout.
 * Compatible with SurrealDB HTTP /sql endpoint.
 */

export class SurrealError extends Error {
	constructor(message, { status, body } = {}) {
		super(message);
		this.name = 'SurrealError';
		this.status = status;
		this.body = body;
	}
}

function connInfo() {
	const url = (process.env.SURREAL_URL || 'http://localhost:8000').replace(/\/$/, '');
	const ns = process.env.SURREAL_NS || 'darbees';
	const db = process.env.SURREAL_DB || 'knowledge';
	const user = process.env.SURREAL_USER || 'root';
	const pass = process.env.SURREAL_PASS || 'password';
	const auth = `Basic ${Buffer.from(`${user}:${pass}`).toString('base64')}`;
	return { url, ns, db, auth };
}

export async function runSurrealQL(query, bindVars = {}, { timeoutMs = 30_000 } = {}) {
	const { url, ns, db, auth } = connInfo();
	const endpoint = `${url}/sql`;
	const signal = AbortSignal.timeout(timeoutMs);

	let response;
	try {
		response = await fetch(endpoint, {
			method: 'POST',
			headers: {
				'content-type': 'application/json',
				Accept: 'application/json',
				Authorization: auth,
				NS: ns,
				DB: db,
			},
			body: JSON.stringify(bindVars ? [query, bindVars] : [query]),
			signal,
		});
	} catch (cause) {
		if (cause?.name === 'AbortError' || cause?.name === 'TimeoutError') {
			throw new SurrealError(`SurrealDB timeout after ${timeoutMs}ms: ${endpoint}`);
		}
		throw new SurrealError(`SurrealDB unreachable at ${endpoint}: ${cause.message}`);
	}

	const text = await response.text();
	let parsed;
	try {
		parsed = text ? JSON.parse(text) : null;
	} catch {
		parsed = text;
	}

	if (!response.ok) {
		throw new SurrealError(`SurrealDB ${response.status}: ${text.slice(0, 200)}`, {
			status: response.status,
			body: parsed,
		});
	}

	// SurrealDB /sql endpoint returns an array of results, one per statement.
	// We usually issue one statement.
	return parsed && Array.isArray(parsed) ? (parsed[0]?.result ?? []) : [];
}