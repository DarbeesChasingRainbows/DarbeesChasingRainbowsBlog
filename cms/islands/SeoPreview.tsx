import { useEffect, useState } from "preact/hooks";

interface SeoPreviewProps {
  initialTitle: string;
  initialDescription: string;
}

export default function SeoPreview({ initialTitle, initialDescription }: SeoPreviewProps) {
  const [title, setTitle] = useState(initialTitle);
  const [description, setDescription] = useState(initialDescription);

  useEffect(() => {
    const handleInput = (e: Event) => {
      const target = e.target as HTMLInputElement | HTMLTextAreaElement;
      if (target.name === "title") {
        setTitle(target.value);
      } else if (target.name === "description") {
        setDescription(target.value);
      }
    };

    // Also listen for custom updates from GeoOptimizer
    const handleSync = () => {
      const titleEl = document.querySelector('input[name="title"]') as HTMLInputElement;
      const descEl = document.querySelector('textarea[name="description"]') as HTMLTextAreaElement;
      if (titleEl) setTitle(titleEl.value);
      if (descEl) setDescription(descEl.value);
    };

    document.addEventListener("input", handleInput);
    
    // Check every second for programmatic updates (like from GeoOptimizer)
    const interval = setInterval(handleSync, 1000);

    return () => {
      document.removeEventListener("input", handleInput);
      clearInterval(interval);
    };
  }, []);

  return (
    <div class="mt-8 space-y-8 border-t border-stone-200 pt-8">
      <h2 class="text-xl font-bold text-stone-900">Interactive Previews</h2>
      
      <div class="grid gap-8 md:grid-cols-2">
        {/* Google Preview */}
        <section class="space-y-4">
          <h3 class="flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-stone-500">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12.48 10.92v3.28h7.84c-.24 1.84-2.21 5.39-7.84 5.39-4.84 0-8.74-4.01-8.74-8.91s3.9-8.91 8.74-8.91c2.76 0 4.6 1.17 5.66 2.19l2.58-2.48c-1.66-1.54-4.22-3.08-8.24-3.08-6.63 0-12 5.37-12 12s5.37 12 12 12c6.91 0 11.52-4.86 11.52-11.72 0-.78-.08-1.38-.24-1.97h-11.28z"/>
            </svg>
            Google Search
          </h3>
          <div class="rounded-xl border border-stone-200 bg-white p-5 shadow-sm">
            <div class="flex items-center gap-2 text-sm text-stone-600 mb-1">
              <div class="flex h-6 w-6 items-center justify-center rounded-full bg-stone-100 text-[10px] font-bold">D</div>
              <span>darbeeschasingrainbows.com</span>
            </div>
            <div class="text-xl text-blue-800 font-medium hover:underline cursor-pointer line-clamp-1">
              {title || "Post Title"}
            </div>
            <div class="mt-1 text-[14px] leading-relaxed text-stone-600 line-clamp-2">
              {description || "The post description will appear here as a snippet in search results..."}
            </div>
          </div>
        </section>

        {/* Twitter/X Card */}
        <section class="space-y-4">
          <h3 class="flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-stone-500">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="currentColor">
              <path d="M18.244 2.25h3.308l-7.227 7.689 8.502 11.236h-6.657l-5.214-6.817L4.99 21.25H1.68l7.73-8.235L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z"/>
            </svg>
            Twitter / X Card
          </h3>
          <div class="overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-sm">
            <div class="aspect-1.91/1 bg-stone-100 flex items-center justify-center border-b border-stone-200">
               <span class="text-stone-400 text-xs font-medium">Post Image Preview</span>
            </div>
            <div class="p-4">
              <div class="text-[13px] uppercase tracking-wide text-stone-500 mb-1">darbeeschasingrainbows.com</div>
              <div class="text-lg font-bold text-stone-900 line-clamp-2 leading-tight">
                {title || "Post Title"}
              </div>
              <div class="mt-1 text-[14px] leading-snug text-stone-600 line-clamp-2">
                {description || "Your post description will be shown here..."}
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
