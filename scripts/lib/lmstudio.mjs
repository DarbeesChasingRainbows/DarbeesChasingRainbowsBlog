/**
 * Thin fetch-based client for the LM Studio OpenAI-compatible API.
 * `fetch` is injectable for tests; everything else falls back to env vars.
 */
const DEFAULT_BASE_URL = 'http://localhost:1234/v1';

export function createClient({
	fetch = globalThis.fetch,
	baseUrl = process.env.LMSTUDIO_URL || DEFAULT_BASE_URL,
	apiKey = process.env.LMSTUDIO_API_KEY || '',
	chatModel = process.env.AI_MODEL_ID || 'local-model',
	visionModel = process.env.AI_VISION_MODEL_ID || '',
	embeddingModel = process.env.AI_EMBEDDING_MODEL_ID || '',
} = {}) {
	const base = baseUrl.replace(/\/$/, '');

	async function post(path, body) {
		const res = await fetch(`${base}${path}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${apiKey}` },
			body: JSON.stringify(body),
		});
		if (!res.ok) {
			throw new Error(`LM Studio ${path} failed: ${res.status} ${await res.text()}`);
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
			const data = await post('/chat/completions', { model, messages, temperature, stream: false });
			return data.choices[0].message.content;
		},

		async chatJson(messages, schema, { model = chatModel, temperature = 0.4 } = {}) {
			const data = await post('/chat/completions', {
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
			const data = await post('/embeddings', { model: embeddingModel, input });
			const vectors = data.data.map((d) => d.embedding);
			return isArray ? vectors : vectors[0];
		},

		async vision(imageBuffer, prompt, schema, { model = visionModel } = {}) {
			if (!model) throw new Error('AI_VISION_MODEL_ID is not set');
			const dataUrl = `data:image/jpeg;base64,${imageBuffer.toString('base64')}`;
			const data = await post('/chat/completions', {
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
			const res = await fetch(`${base}/models`, {
				headers: { Authorization: `Bearer ${apiKey}` },
			});
			if (!res.ok) throw new Error(`LM Studio /models failed: ${res.status}`);
			return res.json();
		},
	};
}
