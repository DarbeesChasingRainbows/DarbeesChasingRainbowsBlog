import { createDefine } from "jsr:@fresh/core@^2.3.3";

const define = createDefine();

export const handler = define.handlers({
  async POST(ctx) {
    try {
      const { text } = await ctx.req.json();

      if (!text) {
        return new Response(JSON.stringify({ error: "No content provided" }), {
          status: 400,
          headers: { "Content-Type": "application/json" },
        });
      }

      // LM Studio OpenAI-compatible endpoint
      const LM_STUDIO_URL = "http://localhost:1234/v1/chat/completions";

      const systemPrompt = `You are an expert in Generative Engine Optimization (GEO).
Analyze the provided content and extract high-density, factual metadata optimized for RAG systems and generative search engines.
Focus on information density and factual accuracy. Avoid flowery language.

Return a valid JSON object with EXACTLY these fields:
- aiSummary: A dense, factual 2-3 sentence summary optimized for information retrieval.
- keyTakeaways: A list of 3-5 key facts or insights.
- entityMentions: A list of 5-10 core entities (people, places, things, concepts) mentioned.
- faq: 2-3 Q&A pairs formatted as "Q: ...\\nA: ..." (use actual newlines or \\n).

Return ONLY the JSON object, no other text or explanation.`;

      const response = await fetch(LM_STUDIO_URL, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          model: "local-model", // LM Studio uses whatever is loaded
          messages: [
            { role: "system", content: systemPrompt },
            { role: "user", content: `Analyze this content:\n\n${text}` },
          ],
          temperature: 0.1, // Low temperature for factual extraction
          response_format: { type: "json_object" },
        }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error("LM Studio error:", errorText);
        return new Response(
          JSON.stringify({ error: "Failed to communicate with LM Studio" }),
          {
            status: 502,
            headers: { "Content-Type": "application/json" },
          },
        );
      }

      const result = await response.json();
      const choice = result.choices[0];
      const geoData = JSON.parse(choice.message.content);

      return new Response(JSON.stringify(geoData), {
        headers: { "Content-Type": "application/json" },
      });
    } catch (error) {
      console.error("GEO API Error:", error);
      return new Response(JSON.stringify({ error: "Internal server error" }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }
  },
});
