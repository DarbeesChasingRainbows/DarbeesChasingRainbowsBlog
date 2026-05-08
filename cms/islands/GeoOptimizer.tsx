import { useState } from "preact/hooks";

export default function GeoOptimizer() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const optimize = async () => {
    const bodyEl = document.querySelector('textarea[name="body"]') as HTMLTextAreaElement;
    if (!bodyEl) return;

    const text = bodyEl.value;
    if (!text || text.length < 50) {
      setError("Content too short to optimize.");
      return;
    }

    setLoading(true);
    setError("");

    try {
      const resp = await fetch("/api/geo", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text }),
      });

      if (!resp.ok) {
        throw new Error(await resp.text() || "Failed to generate GEO data");
      }

      const data = await resp.json();

      // Populate form fields
      if (data.aiSummary) {
        const el = document.querySelector('[name="aiSummary"]') as HTMLTextAreaElement;
        if (el) el.value = data.aiSummary;
      }

      if (data.keyTakeaways) {
        const el = document.querySelector('[name="keyTakeaways"]') as HTMLTextAreaElement;
        if (el) {
          el.value = Array.isArray(data.keyTakeaways) 
            ? data.keyTakeaways.join("\n") 
            : data.keyTakeaways;
        }
      }

      if (data.entityMentions) {
        const el = document.querySelector('[name="entityMentions"]') as HTMLInputElement;
        if (el) {
          el.value = Array.isArray(data.entityMentions) 
            ? data.entityMentions.join(", ") 
            : data.entityMentions;
        }
      }

      if (data.faq) {
        const el = document.querySelector('[name="faq"]') as HTMLTextAreaElement;
        if (el) el.value = data.faq;
      }

    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : "Error generating GEO data");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div class="col-span-full mb-4">
      <button
        type="button"
        onClick={optimize}
        disabled={loading}
        class={`inline-flex items-center gap-2 rounded-md px-4 py-2 text-sm font-semibold text-white transition-colors ${
          loading ? "bg-stone-400 cursor-not-allowed" : "bg-emerald-600 hover:bg-emerald-700"
        }`}
      >
        {loading ? (
          <>
            <svg class="animate-spin h-4 w-4" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" fill="none" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            Generating...
          </>
        ) : (
          <>
            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
            Auto-Generate GEO Data (LM Studio)
          </>
        )}
      </button>
      {error && <p class="mt-2 text-sm text-red-600">{error}</p>}
    </div>
  );
}
