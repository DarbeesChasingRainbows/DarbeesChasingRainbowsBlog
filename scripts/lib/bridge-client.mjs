/**
 * Tiny HTTP wrapper for the dais-bridge gateway. Used by rag-reindex.mjs
 * and (optionally) rag-search.mjs. Throws BridgeError on non-2xx so
 * callers can inspect structured error responses from the bridge.
 */

const DEFAULT_BRIDGE_URL = process.env.BRIDGE_URL || 'http://localhost:5000';

export class BridgeError extends Error {
	constructor(message, { status, body } = {}) {
		super(message);
		this.status = status;
		this.body = body;
	}
}

export async function bridgePost(path, body, { bridgeUrl = DEFAULT_BRIDGE_URL } = {}) {
	const url = `${bridgeUrl.replace(/\/$/, '')}${path}`;
	let response;
	try {
		response = await fetch(url, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify(body),
		});
	} catch (cause) {
		throw new BridgeError(`bridge unreachable at ${url}: ${cause.message}`);
	}
	const text = await response.text();
	let parsed;
	try {
		parsed = text ? JSON.parse(text) : null;
	} catch {
		parsed = text;
	}
	if (!response.ok) {
		throw new BridgeError(`bridge ${response.status}: ${text}`, {
			status: response.status,
			body: parsed,
		});
	}
	return parsed;
}
