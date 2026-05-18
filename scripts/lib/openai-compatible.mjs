/**
 * Thin fetch-based client for any OpenAI-compatible local server
 * (llama.cpp `llama-server` is the primary target; LM Studio and Ollama
 * with its OpenAI shim work too).
 *
 * `fetch` is injectable for tests; everything else falls back to env vars.
 *
 * Multi-server deployments: llama.cpp serves one model per process, so
 * chat / embedding / vision typically run on separate ports. Override
 * the per-task base URLs with LLM_CHAT_URL / LLM_EMBEDDING_URL /
 * LLM_VISION_URL; otherwise all three fall back to the global baseUrl.
 *
 * Back-compat: `LMSTUDIO_URL` and `LMSTUDIO_API_KEY` are still honored
 * but emit a one-time deprecation warning at client-creation time.
 */
const DEFAULT_BASE_URL = 'http://localhost:8080/v1';

let warnedLmstudioUrl = false;
let warnedLmstudioApiKey = false;

function resolveBaseUrl() {
	if (process.env.LLM_CHAT_URL) return process.env.LLM_CHAT_URL;
	if (process.env.LMSTUDIO_URL) {
		if (!warnedLmstudioUrl) {
			console.warn(
				'[openai-compatible] LMSTUDIO_URL is deprecated; set LLM_CHAT_URL (or pass baseUrl) instead.',
			);
			warnedLmstudioUrl = true;
		}
		return process.env.LMSTUDIO_URL;
	}
	return DEFAULT_BASE_URL;
}

function resolveApiKey() {
	if (process.env.AI_API_KEY) return process.env.AI_API_KEY;
	if (process.env.LMSTUDIO_API_KEY) {
		if (!warnedLmstudioApiKey) {
			console.warn(
				'[openai-compatible] LMSTUDIO_API_KEY is deprecated; set AI_API_KEY instead.',
			);
			warnedLmstudioApiKey = true;
		}
		return process.env.LMSTUDIO_API_KEY;
	}
	return '';
}

export function createClient({
	fetch = globalThis.fetch,
	baseUrl = resolveBaseUrl(),
	chatBaseUrl = process.env.LLM_CHAT_URL,
	embeddingBaseUrl = process.env.LLM_EMBEDDING_URL,
	visionBaseUrl = process.env.LLM_VISION_URL,
	apiKey = resolveApiKey(),
	chatModel = process.env.AI_MODEL_ID || 'llama-4-maverick',
	visionModel = process.env.AI_VISION_MODEL_ID || 'qwen/qwen3-vl-8b-instruct',
	embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || 'qwen3-embedding-8b',
} = {}) {
	const trim = (u) => u.replace(/\/$/, '');
	const base = trim(baseUrl);
	const chatBase = trim(chatBaseUrl || baseUrl);
	const embeddingBase = trim(embeddingBaseUrl || baseUrl);
	const visionBase = trim(visionBaseUrl || baseUrl);

	function authHeaders() {
		return apiKey ? { Authorization: `Bearer ${apiKey}` } : {};
	}

	async function post(target, path, body) {
		const res = await fetch(`${target}${path}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json', ...authHeaders() },
			body: JSON.stringify(body),
		});
		if (!res.ok) {
			throw new Error(`LLM ${path} failed: ${res.status} ${await res.text()}`);
		}
		return res.json();
	}

	function requireKeys(obj, keys, where) {
		for (const k of keys) {
			if (!(k in obj)) throw new Error(`LLM ${where}: response missing key "${k}"`);
		}
		return obj;
	}

	function jsonResponseFormat(schema) {
		return {
			type: 'json_schema',
			json_schema: { name: schema.name, schema: schema.shape },
		};
	}

	return {
		async chat(messages, { model = chatModel, temperature = 0.7 } = {}) {
			const data = await post(chatBase, '/chat/completions', {
				model,
				messages,
				temperature,
				stream: false,
			});
			return data.choices[0].message.content;
		},

		async chatJson(messages, schema, { model = chatModel, temperature = 0.4 } = {}) {
			const data = await post(chatBase, '/chat/completions', {
				model,
				messages,
				temperature,
				stream: false,
				response_format: jsonResponseFormat(schema),
			});
			const parsed = JSON.parse(data.choices[0].message.content);
			return requireKeys(parsed, schema.required ?? [], 'chatJson');
		},

		async embed(textOrTexts) {
			const isArray = Array.isArray(textOrTexts);
			const input = isArray ? textOrTexts : [textOrTexts];
			const data = await post(embeddingBase, '/embeddings', { model: embeddingModel, input });
			const vectors = data.data.map((d) => d.embedding);
			return isArray ? vectors : vectors[0];
		},

		async vision(imageBuffer, prompt, schema, { model = visionModel } = {}) {
			if (!model) throw new Error('AI_VISION_MODEL_ID is not set');
			const dataUrl = `data:image/jpeg;base64,${imageBuffer.toString('base64')}`;
			const data = await post(visionBase, '/chat/completions', {
				model,
				stream: false,
				messages: [
					{
						role: 'user',
						content: [
							{ type: 'text', text: prompt },
							{ type: 'image_url', image_url: { url: dataUrl } },
						],
					},
				],
				response_format: jsonResponseFormat(schema),
			});
			const parsed = JSON.parse(data.choices[0].message.content);
			return requireKeys(parsed, schema.required ?? [], 'vision');
		},

		async listModels() {
			const res = await fetch(`${base}/models`, { headers: authHeaders() });
			if (!res.ok) throw new Error(`LLM /models failed: ${res.status}`);
			return res.json();
		},
	};
}
