/**
 * Thin fetch-based client for any OpenAI-compatible local server
 * (llama.cpp `llama-server`, LM Studio, Ollama with OpenAI shim, etc.).
 *
 * `fetch` is injectable for tests; everything else falls back to env vars.
 *
 * Multi-server deployments: llama.cpp serves one model per process, so
 * chat / embedding / vision typically run on separate ports. Override
 * the per-task base URLs with LLM_CHAT_URL / LLM_EMBEDDING_URL /
 * LLM_VISION_URL; otherwise all three fall back to LMSTUDIO_URL.
 */
const DEFAULT_BASE_URL = 'http://localhost:8080/v1';

export function createClient({
	fetch = globalThis.fetch,
	baseUrl = process.env.LMSTUDIO_URL || DEFAULT_BASE_URL,
	chatBaseUrl = process.env.LLM_CHAT_URL,
	embeddingBaseUrl = process.env.LLM_EMBEDDING_URL,
	visionBaseUrl = process.env.LLM_VISION_URL,
	apiKey = process.env.LMSTUDIO_API_KEY || '',
	chatModel = process.env.AI_MODEL_ID || 'local-model',
	visionModel = process.env.AI_VISION_MODEL_ID || 'qwen/qwen3-vl-8b-instruct',
	embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || 'text-embedding-qwen3-embedding-8b',
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
			if (!(k in obj)) throw new Error(`LM Studio ${where}: response missing key "${k}"`);
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
