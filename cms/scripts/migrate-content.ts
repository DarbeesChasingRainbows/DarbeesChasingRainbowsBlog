/**
 * Content Migration Script
 *
 * Reads existing MDX files from src/content/ and imports them into Directus.
 * Parses frontmatter, splits MDX body into body_blocks, and creates items.
 *
 * Run once after schema setup:
 *   npx tsx scripts/migrate-content.ts
 */

import 'dotenv/config';
import { readFileSync, readdirSync, existsSync } from 'fs';
import { join, resolve } from 'path';
import matter from 'gray-matter';

const DIRECTUS_URL = process.env.DIRECTUS_URL || 'http://localhost:8055';
const ADMIN_EMAIL = process.env.DIRECTUS_ADMIN_EMAIL || 'admin@darbees.com';
const ADMIN_PASSWORD = process.env.DIRECTUS_ADMIN_PASSWORD || 'localdev';
const CONTENT_DIR = resolve(join(process.cwd(), '..', 'src', 'content'));

// ─── Auth ───────────────────────────────────────────────────────────────────

async function getToken(): Promise<string> {
  const res = await fetch(`${DIRECTUS_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: ADMIN_EMAIL, password: ADMIN_PASSWORD }),
  });
  if (!res.ok) throw new Error(`Auth failed: ${res.status} ${await res.text()}`);
  const data = await res.json();
  return data.data.access_token;
}

async function api(token: string, method: string, path: string, body?: unknown) {
  const res = await fetch(`${DIRECTUS_URL}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API ${method} ${path}: ${res.status} ${text}`);
  }

  const text = await res.text();
  if (!text) return null;
  return JSON.parse(text);
}

// ─── MDX Body Parser ────────────────────────────────────────────────────────

interface BodyBlock {
  type: string;
  [key: string]: unknown;
}

/**
 * Parse MDX body content into an array of typed body_blocks.
 * Recognizes: headings, <Callout>, <FieldNotesBlock>, tables, code blocks, and markdown.
 */
function parseMdxBody(body: string): BodyBlock[] {
  // Strip import statements (we'll regenerate them on export)
  const lines = body.split('\n');
  const contentLines: string[] = [];

  for (const line of lines) {
    // Skip import statements
    if (/^import\s+.+\s+from\s+/.test(line.trim())) continue;
    contentLines.push(line);
  }

  const content = contentLines.join('\n').trim();
  if (!content) return [];

  const blocks: BodyBlock[] = [];
  let currentMarkdown = '';

  // Helper to flush accumulated markdown into a block
  function flushMarkdown() {
    const trimmed = currentMarkdown.trim();
    if (trimmed) {
      blocks.push({ type: 'markdown', content: trimmed });
    }
    currentMarkdown = '';
  }

  // Split into segments by component tags and special blocks
  let i = 0;
  const allLines = content.split('\n');

  while (i < allLines.length) {
    const line = allLines[i];
    const trimmedLine = line.trim();

    // ── Heading detection ──
    const headingMatch = trimmedLine.match(/^(#{2,4})\s+(.+)$/);
    if (headingMatch) {
      flushMarkdown();
      blocks.push({
        type: 'heading',
        level: headingMatch[1].length,
        text: headingMatch[2].trim(),
      });
      i++;
      continue;
    }

    // ── <Callout> detection ──
    if (trimmedLine.startsWith('<Callout')) {
      flushMarkdown();

      // Parse props from opening tag
      const typeMatch = trimmedLine.match(/type="([^"]+)"/);
      const titleMatch = trimmedLine.match(/title="([^"]+)"/);

      // Check if self-closing
      if (trimmedLine.endsWith('/>')) {
        blocks.push({
          type: 'callout',
          type_variant: typeMatch?.[1] || 'note',
          title: titleMatch?.[1] || '',
          content: '',
        });
        i++;
        continue;
      }

      // Inline content on same line
      const inlineMatch = trimmedLine.match(/>(.+)<\/Callout>$/);
      if (inlineMatch) {
        blocks.push({
          type: 'callout',
          type_variant: typeMatch?.[1] || 'note',
          title: titleMatch?.[1] || '',
          content: inlineMatch[1].trim(),
        });
        i++;
        continue;
      }

      // Multi-line callout — collect until </Callout>
      const calloutContent: string[] = [];
      i++;
      while (i < allLines.length && !allLines[i].trim().startsWith('</Callout>')) {
        calloutContent.push(allLines[i]);
        i++;
      }
      i++; // Skip closing tag

      blocks.push({
        type: 'callout',
        type_variant: typeMatch?.[1] || 'note',
        title: titleMatch?.[1] || '',
        content: calloutContent.join('\n').trim(),
      });
      continue;
    }

    // ── <FieldNotesBlock> detection ──
    if (trimmedLine.startsWith('<FieldNotesBlock')) {
      flushMarkdown();

      // Collect all lines until /> or </FieldNotesBlock>
      const componentLines: string[] = [trimmedLine];
      if (!trimmedLine.endsWith('/>') && !trimmedLine.endsWith('</FieldNotesBlock>')) {
        i++;
        while (i < allLines.length) {
          componentLines.push(allLines[i]);
          if (allLines[i].trim().endsWith('/>') || allLines[i].trim().endsWith('</FieldNotesBlock>')) {
            break;
          }
          i++;
        }
      }
      i++;

      const fullTag = componentLines.join('\n');

      // Parse props from the JSX
      const locationMatch = fullTag.match(/location="([^"]+)"/);
      const dateMatch = fullTag.match(/date="([^"]+)"/);
      const weatherMatch = fullTag.match(/weather="([^"]+)"/);
      const heardMatch = fullTag.match(/heard="([^"]+)"/);
      const wonderedMatch = fullTag.match(/wondered="([^"]+)"/);
      const learnedMatch = fullTag.match(/learned="([^"]+)"/);

      // Parse saw array: saw={['item1', 'item2']}
      const sawMatch = fullTag.match(/saw=\{?\[([^\]]+)\]\}?/s);
      let saw: string[] = [];
      if (sawMatch) {
        saw = sawMatch[1]
          .split(',')
          .map(s => s.trim().replace(/^['"]|['"]$/g, '').trim())
          .filter(Boolean);
      }

      blocks.push({
        type: 'field_notes_block',
        location: locationMatch?.[1] || '',
        date: dateMatch?.[1] || '',
        weather: weatherMatch?.[1] || '',
        saw,
        heard: heardMatch?.[1] || '',
        wondered: wonderedMatch?.[1] || '',
        learned: learnedMatch?.[1] || '',
      });
      continue;
    }

    // ── Code block detection ──
    if (trimmedLine.startsWith('```')) {
      flushMarkdown();
      const language = trimmedLine.slice(3).trim();
      const codeLines: string[] = [];
      i++;
      while (i < allLines.length && !allLines[i].trim().startsWith('```')) {
        codeLines.push(allLines[i]);
        i++;
      }
      i++; // Skip closing ```

      blocks.push({
        type: 'code',
        language,
        content: codeLines.join('\n'),
      });
      continue;
    }

    // ── Table detection ──
    if (trimmedLine.startsWith('|') && trimmedLine.endsWith('|')) {
      // Look ahead for separator row
      const nextLine = allLines[i + 1]?.trim() || '';
      if (nextLine.startsWith('|') && nextLine.includes('---')) {
        flushMarkdown();

        // Parse headers
        const headers = trimmedLine.split('|').filter(Boolean).map(h => h.trim());

        // Skip separator row
        i += 2;

        // Parse data rows
        const rows: string[][] = [];
        while (i < allLines.length && allLines[i].trim().startsWith('|')) {
          const cells = allLines[i].split('|').filter(Boolean).map(c => c.trim());
          rows.push(cells);
          i++;
        }

        blocks.push({
          type: 'table',
          headers,
          rows,
        });
        continue;
      }
    }

    // ── Default: accumulate as markdown ──
    currentMarkdown += line + '\n';
    i++;
  }

  flushMarkdown();
  return blocks;
}

// ─── Frontmatter → Directus Field Mapping ───────────────────────────────────

interface FieldMapping {
  [astroField: string]: string;
}

const BLOG_MAPPING: FieldMapping = {
  title: 'title',
  description: 'description',
  pubDate: 'pub_date',
  updatedDate: 'updated_date',
  author: 'author',
  category: 'category',
  tags: 'tags',
  imageAlt: 'image_alt',
  preview: 'preview',
  aiSummary: 'ai_summary',
  keyTakeaways: 'key_takeaways',
  entityMentions: 'entity_mentions',
  faq: 'faq',
  sources: 'sources',
  draft: '_draft',
};

const BOOK_MAPPING: FieldMapping = {
  ...BLOG_MAPPING,
  bookTitle: 'book_title',
  author: 'book_author', // In books, author = book author
  ageRange: 'age_range',
  formatUsed: 'format_used',
  rating: 'rating',
  dadTake: 'dad_take',
  momTake: 'mom_take',
  kidsTake: 'kids_take',
  readAloudValue: 'read_aloud_value',
  audiobookValue: 'audiobook_value',
  educationalValue: 'educational_value',
  contentNotes: 'content_notes',
  worldviewNotes: 'worldview_notes',
};

const PROJECT_MAPPING: FieldMapping = {
  ...BLOG_MAPPING,
  difficulty: 'difficulty',
  estimatedCost: 'estimated_cost',
  estimatedTime: 'estimated_time',
  githubUrl: 'github_url',
  partsList: 'parts_list',
};

const FIELD_NOTE_MAPPING: FieldMapping = {
  ...BLOG_MAPPING,
  location: 'location',
  region: 'region',
  weather: 'weather',
  includesHomeschool: 'includes_homeschool',
};

interface CollectionConfig {
  astroFolder: string;
  directusCollection: string;
  mapping: FieldMapping;
}

const COLLECTIONS: CollectionConfig[] = [
  { astroFolder: 'blog', directusCollection: 'blog_posts', mapping: BLOG_MAPPING },
  { astroFolder: 'books', directusCollection: 'book_reviews', mapping: BOOK_MAPPING },
  { astroFolder: 'projects', directusCollection: 'projects', mapping: PROJECT_MAPPING },
  { astroFolder: 'field-notes', directusCollection: 'field_notes', mapping: FIELD_NOTE_MAPPING },
];

/**
 * Map Astro frontmatter to Directus fields.
 */
function mapFrontmatter(data: Record<string, unknown>, mapping: FieldMapping): Record<string, unknown> {
  const result: Record<string, unknown> = {};

  for (const [astroKey, directusKey] of Object.entries(mapping)) {
    if (directusKey === '_draft') continue; // Handle draft separately
    if (data[astroKey] !== undefined && data[astroKey] !== null) {
      result[directusKey] = data[astroKey];
    }
  }

  // Handle imageAttribution nested object → flat fields
  if (data.imageAttribution && typeof data.imageAttribution === 'object') {
    const attr = data.imageAttribution as Record<string, string>;
    if (attr.name) result.image_attribution_name = attr.name;
    if (attr.url) result.image_attribution_url = attr.url;
  }

  // Handle date formatting
  if (result.pub_date instanceof Date) {
    result.pub_date = (result.pub_date as Date).toISOString().split('T')[0];
  }
  if (result.updated_date instanceof Date) {
    result.updated_date = (result.updated_date as Date).toISOString().split('T')[0];
  }

  // Handle draft → status
  result.status = data.draft === false ? 'published' : 'draft';

  return result;
}

// ─── Main Migration ─────────────────────────────────────────────────────────

async function main() {
  console.log(`\n📦 Migrating content from ${CONTENT_DIR} to Directus\n`);

  const token = await getToken();
  console.log('✅ Authenticated\n');

  let totalMigrated = 0;
  let totalErrors = 0;

  for (const col of COLLECTIONS) {
    const folder = join(CONTENT_DIR, col.astroFolder);
    if (!existsSync(folder)) {
      console.log(`⚠ Skipping ${col.astroFolder} — directory not found`);
      continue;
    }

    const files = readdirSync(folder).filter(f => f.endsWith('.mdx') && !f.startsWith('_'));
    console.log(`📁 ${col.astroFolder}: ${files.length} files`);

    for (const file of files) {
      const filePath = join(folder, file);
      const slug = file.replace(/\.mdx$/, '');

      try {
        const raw = readFileSync(filePath, 'utf-8');
        const { data: frontmatter, content: body } = matter(raw);

        // Map frontmatter
        const directusFields = mapFrontmatter(frontmatter, col.mapping);
        directusFields.slug = slug;

        // Parse body into blocks
        directusFields.body_blocks = parseMdxBody(body);

        // Set GEO status based on whether GEO fields are populated
        if (directusFields.ai_summary) {
          directusFields.geo_status = 'generated';
        } else {
          directusFields.geo_status = 'none';
        }

        // Check if item already exists
        try {
          const existing = await api(token, 'GET', `/items/${col.directusCollection}?filter[slug][_eq]=${slug}&limit=1`);
          if (existing.data && existing.data.length > 0) {
            console.log(`  ⚠ ${slug} — already exists, skipping`);
            continue;
          }
        } catch {
          // Collection might not have items yet, continue
        }

        // Create item
        await api(token, 'POST', `/items/${col.directusCollection}`, directusFields);
        console.log(`  ✅ ${slug} (${(directusFields.body_blocks as BodyBlock[]).length} blocks)`);
        totalMigrated++;
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error(`  ❌ ${slug}: ${msg}`);
        totalErrors++;
      }
    }

    console.log('');
  }

  console.log(`\n📊 Migration complete: ${totalMigrated} migrated, ${totalErrors} errors\n`);
}

main().catch(err => {
  console.error('❌ Migration failed:', err.message);
  process.exit(1);
});
