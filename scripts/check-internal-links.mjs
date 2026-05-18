#!/usr/bin/env node
/**
 * Static internal-link checker for the production build in `dist/`.
 *
 * Walks every .html file, extracts every internal href, and verifies that
 * the target maps to a real file under `dist/`. Exits non-zero (and prints
 * a list of broken links with their source page) when any internal link
 * fails to resolve.
 *
 * Why this exists:
 *   The `/newsletter` link in `kingdom-farm.astro` shipped to "production"
 *   even though that route never existed. The build didn't catch it because
 *   Astro doesn't validate internal hrefs by default. This script does.
 *
 * What it considers "internal":
 *   - href starts with "/" (treated as a path under dist/)
 *   - href starts with "./" or "../" (resolved relative to the source file)
 *
 * What it ignores:
 *   - http(s)://, mailto:, tel:, javascript:, data:, # (in-page anchors)
 *   - href starts with "//" (protocol-relative)
 *
 * What we DON'T verify:
 *   - That a "#fragment" exists on the target page (would require parsing
 *     each target for matching id/name attributes — not worth the cost).
 *     We treat /foo/#bar the same as /foo/.
 *
 * Resolution rules:
 *   - "/about/" → dist/about/index.html (also accepts dist/about.html)
 *   - "/rss.xml" → dist/rss.xml
 *   - "/" → dist/index.html
 */
import { readFile, readdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join, resolve, dirname, posix } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const DIST = resolve(__dirname, '..', 'dist');

if (!existsSync(DIST)) {
	console.error(`✖ ${DIST} does not exist. Run \`npm run build\` first.`);
	process.exit(2);
}

/** Recursively walk a directory and yield .html files (relative to root). */
async function* walkHtml(root, dir = root) {
	for (const entry of await readdir(dir, { withFileTypes: true })) {
		const full = join(dir, entry.name);
		if (entry.isDirectory()) {
			yield* walkHtml(root, full);
		} else if (entry.isFile() && entry.name.endsWith('.html')) {
			yield full;
		}
	}
}

/** Extract href values from raw HTML. Crude but sufficient for our output. */
function extractHrefs(html) {
	// Strip <script>…</script> blocks first so that href= strings inside
	// bundled JavaScript are not mistaken for HTML anchor attributes.
	const stripped = html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '');
	const out = [];
	const re = /\bhref=("[^"]*"|'[^']*'|[^\s>]+)/gi;
	let m;
	while ((m = re.exec(stripped)) !== null) {
		out.push(m[1].replace(/^['"]|['"]$/g, ''));
	}
	return out;
}

function isInternal(href) {
	if (!href) return false;
	if (href.startsWith('#')) return false;
	if (href.startsWith('mailto:')) return false;
	if (href.startsWith('tel:')) return false;
	if (href.startsWith('javascript:')) return false;
	if (href.startsWith('data:')) return false;
	if (href.startsWith('//')) return false;
	if (/^https?:\/\//i.test(href)) return false;
	return true;
}

/** Strip query string and fragment from a URL path. */
function cleanPath(href) {
	return href.split('#')[0].split('?')[0];
}

/**
 * Map a logical URL path to candidate filesystem paths under DIST.
 *   "/foo/"       → "dist/foo/index.html"
 *   "/foo"        → "dist/foo/index.html" OR "dist/foo.html"
 *   "/rss.xml"    → "dist/rss.xml"
 *   "/"           → "dist/index.html"
 *
 * Astro writes dynamic-param folder names verbatim (e.g. "Faith & Reflections")
 * but href values in the HTML are URL-encoded ("Faith%20%26%20Reflections").
 * Static hosts decode the request URL before resolving filesystem paths, so
 * the link works in production. We mirror that by trying BOTH the raw URL
 * path and its decoded form.
 */
function candidateFiles(urlPath) {
	const path = urlPath === '' ? '/' : urlPath;
	if (path === '/') return [join(DIST, 'index.html')];

	const variants = new Set([path]);
	try {
		variants.add(decodeURIComponent(path));
	} catch {
		// Malformed URL encoding — keep the raw form only.
	}

	const candidates = [];
	for (const p of variants) {
		const trimmed = p.replace(/^\//, '');
		if (p.endsWith('/')) {
			candidates.push(join(DIST, trimmed, 'index.html'));
		} else if (/\.[a-z0-9]+$/i.test(trimmed)) {
			candidates.push(join(DIST, trimmed));
		} else {
			candidates.push(join(DIST, trimmed, 'index.html'));
			candidates.push(join(DIST, trimmed + '.html'));
		}
	}
	return candidates;
}

/** Resolve a relative href ("./foo", "../bar") against the source file's dir. */
function resolveRelative(href, sourceFile) {
	const sourceDir = dirname(sourceFile);
	const abs = resolve(sourceDir, cleanPath(href));
	const relFromDist = '/' + posix.normalize(abs.replace(DIST, '').replace(/\\/g, '/'));
	return relFromDist;
}

const broken = [];
let totalLinks = 0;
let pagesScanned = 0;

for await (const file of walkHtml(DIST)) {
	pagesScanned++;
	const html = await readFile(file, 'utf8');
	const hrefs = extractHrefs(html);
	for (const raw of hrefs) {
		if (!isInternal(raw)) continue;
		totalLinks++;
		let urlPath;
		if (raw.startsWith('/')) {
			urlPath = cleanPath(raw);
		} else {
			urlPath = cleanPath(resolveRelative(raw, file));
		}
		const candidates = candidateFiles(urlPath);
		const found = candidates.some((c) => existsSync(c));
		if (!found) {
			broken.push({
				source: file.replace(DIST, '').replace(/\\/g, '/'),
				href: raw,
				resolvedTo: urlPath,
				tried: candidates.map((c) => c.replace(DIST, '').replace(/\\/g, '/')),
			});
		}
	}
}

console.log(`Scanned ${pagesScanned} HTML page(s), checked ${totalLinks} internal link(s).`);

if (broken.length === 0) {
	console.log('✓ No broken internal links.');
	process.exit(0);
}

console.error(`✖ Found ${broken.length} broken internal link(s):\n`);
for (const b of broken) {
	console.error(`  on ${b.source}`);
	console.error(`    href="${b.href}"  →  ${b.resolvedTo}`);
	console.error(`    tried: ${b.tried.join(', ')}\n`);
}
process.exit(1);
