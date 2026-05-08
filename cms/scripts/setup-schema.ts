/**
 * Schema Setup Script
 *
 * Creates all 4 content collections in Directus with proper fields.
 * Run once after starting a fresh Directus instance:
 *   npx tsx scripts/setup-schema.ts
 */

import 'dotenv/config';

const DIRECTUS_URL = process.env.DIRECTUS_URL || 'http://localhost:8055';
const ADMIN_EMAIL = process.env.DIRECTUS_ADMIN_EMAIL || 'admin@darbees.com';
const ADMIN_PASSWORD = process.env.DIRECTUS_ADMIN_PASSWORD || 'localdev';

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
    // Ignore "already exists" errors
    if (res.status === 400 && text.includes('already exists')) {
      console.log(`  ⚠ Already exists: ${path}`);
      return null;
    }
    throw new Error(`API ${method} ${path} failed: ${res.status} ${text}`);
  }

  return res.json();
}

// ─── Shared field definitions ───────────────────────────────────────────────

const CONTENT_CATEGORIES = [
  'RV Life', 'Homeschool', 'Kingdom Farm',
  'Faith & Reflections', 'Field Notes', 'Projects & Builds',
];

const BOOK_CATEGORIES = [
  'Kids', 'Tweens', 'Teens', 'Family',
  'Homeschool', 'Kingdom Farm', 'Theology', 'Nature', 'History',
];

/** Shared core fields that every collection gets */
function coreFields(collection: string, categories: string[]) {
  return [
    { field: 'sort', type: 'integer', meta: { hidden: true } },
    { field: 'slug', type: 'string', meta: { interface: 'input', required: true, options: { trim: true, slug: true }, note: 'URL slug — kebab-case, becomes the filename' }, schema: { is_unique: true, is_nullable: false } },
    { field: 'status', type: 'string', meta: { interface: 'select-dropdown', options: { choices: [{ text: 'Draft', value: 'draft' }, { text: 'Published', value: 'published' }] }, width: 'half', default_value: 'draft' }, schema: { default_value: 'draft' } },
    { field: 'title', type: 'string', meta: { interface: 'input', required: true, width: 'full', note: 'Page title — aim for 60 characters' }, schema: { is_nullable: false } },
    { field: 'description', type: 'text', meta: { interface: 'input-multiline', required: true, note: 'Meta description — aim for 155 characters' }, schema: { is_nullable: false } },
    { field: 'pub_date', type: 'date', meta: { interface: 'datetime', required: true, width: 'half' }, schema: { is_nullable: false } },
    { field: 'updated_date', type: 'date', meta: { interface: 'datetime', width: 'half' } },
    { field: 'category', type: 'string', meta: { interface: 'select-dropdown', required: true, width: 'half', options: { choices: categories.map(c => ({ text: c, value: c })) } }, schema: { is_nullable: false } },
    { field: 'tags', type: 'json', meta: { interface: 'tags', width: 'full', note: 'Comma-separated tags' } },
    { field: 'hero_image', type: 'uuid', meta: { interface: 'file-image', width: 'half', note: 'Main hero image' }, schema: {} },
    { field: 'featured_image', type: 'uuid', meta: { interface: 'file-image', width: 'half', note: 'Smaller featured/card image' }, schema: {} },
    { field: 'image_alt', type: 'text', meta: { interface: 'input-multiline', note: 'Descriptive alt text for the hero image' } },
    { field: 'image_attribution_name', type: 'string', meta: { interface: 'input', width: 'half', note: 'Image credit name' } },
    { field: 'image_attribution_url', type: 'string', meta: { interface: 'input', width: 'half', note: 'Image credit URL' } },
    { field: 'preview', type: 'string', meta: { interface: 'input', note: 'Short preview text (max 200 chars)', options: { trim: true } } },
  ];
}

/** Body blocks field — the structured content editor */
function bodyBlocksField() {
  return {
    field: 'body_blocks',
    type: 'json',
    meta: {
      interface: 'list',
      note: 'Ordered content blocks — add headings, prose, callouts, tables, images, and code',
      options: {
        template: '{{type}}: {{title}}{{text}}',
        fields: [
          {
            field: 'type',
            name: 'Block Type',
            type: 'string',
            meta: {
              interface: 'select-dropdown',
              width: 'half',
              required: true,
              options: {
                choices: [
                  { text: 'Markdown', value: 'markdown' },
                  { text: 'Heading', value: 'heading' },
                  { text: 'Callout', value: 'callout' },
                  { text: 'Table', value: 'table' },
                  { text: 'Image', value: 'image' },
                  { text: 'Code', value: 'code' },
                  { text: 'Carousel', value: 'carousel' },
                  { text: 'Gallery', value: 'gallery' },
                  { text: 'Video Embed', value: 'video' },
                  { text: 'CTA Button', value: 'cta' },
                  { text: 'Blockquote', value: 'blockquote' },
                  { text: 'Accordion (FAQ)', value: 'accordion' },
                ],
              },
            },
          },
          // Heading fields
          { field: 'level', name: 'Heading Level', type: 'integer', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{ text: 'H2', value: 2 }, { text: 'H3', value: 3 }, { text: 'H4', value: 4 }] }, conditions: [{ rule: { type: { _neq: 'heading' } }, hidden: true }] } },
          { field: 'text', name: 'Heading Text', type: 'string', meta: { interface: 'input', width: 'full', conditions: [{ rule: { type: { _neq: 'heading' } }, hidden: true }] } },
          
          // Markdown / Callout / Code content
          { field: 'content', name: 'Content', type: 'text', meta: { interface: 'input-multiline', width: 'full', conditions: [{ rule: { _and: [{ type: { _neq: 'markdown' } }, { type: { _neq: 'callout' } }, { type: { _neq: 'code' } }] }, hidden: true }] } },
          
          // Callout-specific
          { field: 'type_variant', name: 'Callout Type', type: 'string', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{ text: 'Note', value: 'note' }, { text: 'Tip', value: 'tip' }, { text: 'Warning', value: 'warning' }, { text: 'Faith', value: 'faith' }, { text: 'Field Note', value: 'field-note' }, { text: 'Project', value: 'project' }] }, conditions: [{ rule: { type: { _neq: 'callout' } }, hidden: true }] } },
          { field: 'title', name: 'Title', type: 'string', meta: { interface: 'input', width: 'half', conditions: [{ rule: { type: { _neq: 'callout' } }, hidden: true }] } },
          
          // Table fields
          { field: 'headers', name: 'Table Headers', type: 'json', meta: { interface: 'tags', note: 'Column headers', conditions: [{ rule: { type: { _neq: 'table' } }, hidden: true }] } },
          { field: 'rows', name: 'Table Rows', type: 'json', meta: { interface: 'list', note: 'Each row is an array of cell values', conditions: [{ rule: { type: { _neq: 'table' } }, hidden: true }] } },
          
          // Single Image fields
          { field: 'file', name: 'Image File', type: 'uuid', meta: { interface: 'file-image', conditions: [{ rule: { type: { _neq: 'image' } }, hidden: true }] } },
          { field: 'alt', name: 'Alt Text', type: 'string', meta: { interface: 'input', conditions: [{ rule: { type: { _neq: 'image' } }, hidden: true }] } },
          { field: 'caption', name: 'Caption', type: 'string', meta: { interface: 'input', conditions: [{ rule: { type: { _neq: 'image' } }, hidden: true }] } },
          
          // Code fields
          { field: 'language', name: 'Language', type: 'string', meta: { interface: 'input', width: 'half', note: 'e.g. javascript, bash, yaml', conditions: [{ rule: { type: { _neq: 'code' } }, hidden: true }] } },
          
          // Carousel fields
          { field: 'slides', name: 'Slides', type: 'json', meta: { interface: 'list', note: 'Add images to the carousel', conditions: [{ rule: { type: { _neq: 'carousel' } }, hidden: true }], options: { template: '{{alt}}', fields: [ { field: 'file', name: 'Image', type: 'uuid', meta: { interface: 'file-image', required: true } }, { field: 'alt', name: 'Alt Text', type: 'string', meta: { interface: 'input' } }, { field: 'caption', name: 'Caption', type: 'string', meta: { interface: 'input' } } ] } } },
          
          // Gallery fields
          { field: 'images', name: 'Images', type: 'json', meta: { interface: 'list', note: 'Add images to the gallery', conditions: [{ rule: { type: { _neq: 'gallery' } }, hidden: true }], options: { template: '{{alt}}', fields: [ { field: 'file', name: 'Image', type: 'uuid', meta: { interface: 'file-image', required: true } }, { field: 'alt', name: 'Alt Text', type: 'string', meta: { interface: 'input' } }, { field: 'caption', name: 'Caption', type: 'string', meta: { interface: 'input' } } ] } } },
          { field: 'columns', name: 'Columns', type: 'integer', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{ text: '2 Columns', value: 2 }, { text: '3 Columns', value: 3 }, { text: '4 Columns', value: 4 }] }, default_value: 3, conditions: [{ rule: { type: { _neq: 'gallery' } }, hidden: true }] } },

          // Video & CTA fields
          { field: 'url', name: 'URL', type: 'string', meta: { interface: 'input', note: 'YouTube/Vimeo URL or CTA link', conditions: [{ rule: { _and: [{ type: { _neq: 'video' } }, { type: { _neq: 'cta' } }] }, hidden: true }] } },
          { field: 'text', name: 'Button Text', type: 'string', meta: { interface: 'input', width: 'half', conditions: [{ rule: { type: { _neq: 'cta' } }, hidden: true }] } },
          { field: 'style', name: 'Button Style', type: 'string', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{ text: 'Primary', value: 'primary' }, { text: 'Secondary', value: 'secondary' }, { text: 'Accent', value: 'accent' }, { text: 'Outline', value: 'outline' }] }, default_value: 'primary', conditions: [{ rule: { type: { _neq: 'cta' } }, hidden: true }] } },

          // Blockquote fields
          { field: 'quote', name: 'Quote Text', type: 'text', meta: { interface: 'input-multiline', width: 'full', conditions: [{ rule: { type: { _neq: 'blockquote' } }, hidden: true }] } },
          { field: 'author', name: 'Author/Source', type: 'string', meta: { interface: 'input', width: 'half', conditions: [{ rule: { type: { _neq: 'blockquote' } }, hidden: true }] } },
          { field: 'context', name: 'Context/Role', type: 'string', meta: { interface: 'input', width: 'half', conditions: [{ rule: { type: { _neq: 'blockquote' } }, hidden: true }] } },

          // Accordion fields
          { field: 'items', name: 'Accordion Items', type: 'json', meta: { interface: 'list', note: 'Add expandable items', conditions: [{ rule: { type: { _neq: 'accordion' } }, hidden: true }], options: { template: '{{title}}', fields: [ { field: 'title', name: 'Title/Question', type: 'string', meta: { interface: 'input', required: true, width: 'full' } }, { field: 'content', name: 'Content/Answer', type: 'text', meta: { interface: 'input-multiline', required: true, width: 'full' } } ] } } },
        ],
      },
    },
  };
}

/** LLM / GEO fields shared by all collections */
function llmFields() {
  return [
    { field: 'divider_geo', type: 'alias', meta: { interface: 'presentation-divider', options: { title: 'GEO / AI Metadata', icon: 'smart_toy' }, special: ['alias', 'no-data'], width: 'full' } },
    { field: 'geo_status', type: 'string', meta: { interface: 'select-dropdown', width: 'half', default_value: 'none', options: { choices: [{ text: 'None', value: 'none' }, { text: 'Pending', value: 'pending' }, { text: 'Generated', value: 'generated' }, { text: 'Stale', value: 'stale' }, { text: 'Error', value: 'error' }] }, note: 'Set to "Pending" to trigger GEO generation via LM Studio' }, schema: { default_value: 'none' } },
    { field: 'ai_summary', type: 'text', meta: { interface: 'input-multiline', note: 'AI-generated summary — quotable by AI assistants', width: 'full' } },
    { field: 'key_takeaways', type: 'json', meta: { interface: 'tags', note: 'Key facts for AI retrieval' } },
    { field: 'entity_mentions', type: 'json', meta: { interface: 'tags', note: 'Proper nouns: people, places, brands' } },
    {
      field: 'faq', type: 'json', meta: {
        interface: 'list', note: 'FAQ pairs for structured data',
        options: {
          template: '{{question}}',
          fields: [
            { field: 'question', name: 'Question', type: 'string', meta: { interface: 'input', width: 'full', required: true } },
            { field: 'answer', name: 'Answer', type: 'text', meta: { interface: 'input-multiline', width: 'full', required: true } },
          ],
        },
      },
    },
    {
      field: 'sources', type: 'json', meta: {
        interface: 'list', note: 'Source citations',
        options: {
          template: '{{title}} ({{type}})',
          fields: [
            { field: 'title', name: 'Title', type: 'string', meta: { interface: 'input', required: true } },
            { field: 'url', name: 'URL', type: 'string', meta: { interface: 'input', required: true } },
            { field: 'type', name: 'Type', type: 'string', meta: { interface: 'select-dropdown', options: { choices: [{ text: 'Primary', value: 'primary' }, { text: 'Secondary', value: 'secondary' }] } } },
          ],
        },
      },
    },
  ];
}

// ─── Collection Definitions ─────────────────────────────────────────────────

interface CollectionDef {
  name: string;
  note: string;
  icon: string;
  extraFields: Array<Record<string, unknown>>;
}

const COLLECTIONS: CollectionDef[] = [
  {
    name: 'blog_posts',
    note: 'Blog posts — RV life, homeschool, kingdom farm, faith',
    icon: 'article',
    extraFields: [
      { field: 'author', type: 'string', meta: { interface: 'input', width: 'half', default_value: 'The Darbees' }, schema: { default_value: 'The Darbees' } },
    ],
  },
  {
    name: 'book_reviews',
    note: 'Family book reviews with ratings and takes',
    icon: 'menu_book',
    extraFields: [
      { field: 'book_title', type: 'string', meta: { interface: 'input', required: true, note: 'Full book title' }, schema: { is_nullable: false } },
      { field: 'book_author', type: 'string', meta: { interface: 'input', required: true, note: 'Book author name' }, schema: { is_nullable: false } },
      { field: 'age_range', type: 'string', meta: { interface: 'input', width: 'half', note: 'e.g. 8-12' } },
      { field: 'format_used', type: 'string', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{ text: 'Read Aloud', value: 'read-aloud' }, { text: 'Audiobook', value: 'audiobook' }, { text: 'Independent', value: 'independent' }, { text: 'Mixed', value: 'mixed' }] } } },
      { field: 'rating', type: 'string', meta: { interface: 'select-dropdown', required: true, width: 'half', options: { choices: [{ text: '🟢 Green', value: 'green' }, { text: '🟡 Yellow', value: 'yellow' }, { text: '👨 Parent Read', value: 'parent-read' }, { text: '🔴 Red', value: 'red' }] } }, schema: { is_nullable: false } },
      { field: 'divider_takes', type: 'alias', meta: { interface: 'presentation-divider', options: { title: 'Family Takes' }, special: ['alias', 'no-data'], width: 'full' } },
      { field: 'dad_take', type: 'text', meta: { interface: 'input-multiline', width: 'full', note: "One sentence from Dad's perspective" } },
      { field: 'mom_take', type: 'text', meta: { interface: 'input-multiline', width: 'full', note: "One sentence from Mom's perspective" } },
      { field: 'kids_take', type: 'text', meta: { interface: 'input-multiline', width: 'full', note: 'What the kids said about it' } },
      { field: 'divider_values', type: 'alias', meta: { interface: 'presentation-divider', options: { title: 'Value Assessments' }, special: ['alias', 'no-data'], width: 'full' } },
      { field: 'read_aloud_value', type: 'text', meta: { interface: 'input-multiline', note: 'How well it works as a read-aloud' } },
      { field: 'audiobook_value', type: 'text', meta: { interface: 'input-multiline', note: 'Quality of the audiobook production' } },
      { field: 'educational_value', type: 'text', meta: { interface: 'input-multiline', note: 'What kids actually learn' } },
      { field: 'divider_notes', type: 'alias', meta: { interface: 'presentation-divider', options: { title: 'Content & Worldview Notes' }, special: ['alias', 'no-data'], width: 'full' } },
      { field: 'content_notes', type: 'text', meta: { interface: 'input-multiline', note: 'Content parents should be aware of' } },
      { field: 'worldview_notes', type: 'text', meta: { interface: 'input-multiline', note: 'Worldview alignment notes' } },
    ],
  },
  {
    name: 'projects',
    note: 'DIY projects — solar, RV mods, off-grid builds',
    icon: 'construction',
    extraFields: [
      { field: 'difficulty', type: 'string', meta: { interface: 'select-dropdown', width: 'third', options: { choices: [{ text: 'Easy', value: 'easy' }, { text: 'Medium', value: 'medium' }, { text: 'Hard', value: 'hard' }] } } },
      { field: 'estimated_cost', type: 'string', meta: { interface: 'input', width: 'third', note: 'e.g. $340' } },
      { field: 'estimated_time', type: 'string', meta: { interface: 'input', width: 'third', note: 'ISO 8601: PT4H = 4 hours' } },
      { field: 'github_url', type: 'string', meta: { interface: 'input', note: 'GitHub repo URL' } },
      {
        field: 'parts_list', type: 'json', meta: {
          interface: 'list', note: 'Bill of materials',
          options: {
            template: '{{name}} (x{{quantity}})',
            fields: [
              { field: 'name', name: 'Part Name', type: 'string', meta: { interface: 'input', required: true } },
              { field: 'quantity', name: 'Quantity', type: 'integer', meta: { interface: 'input', width: 'half', default_value: 1 } },
              { field: 'url', name: 'Product URL', type: 'string', meta: { interface: 'input' } },
              { field: 'notes', name: 'Notes', type: 'string', meta: { interface: 'input' } },
            ],
          },
        },
      },
    ],
  },
  {
    name: 'field_notes',
    note: 'Outdoor observations — trails, parks, nature',
    icon: 'park',
    extraFields: [
      { field: 'location', type: 'string', meta: { interface: 'input', required: true, note: 'Specific place name' }, schema: { is_nullable: false } },
      { field: 'region', type: 'string', meta: { interface: 'input', width: 'half', note: 'e.g. Northwest Georgia' } },
      { field: 'weather', type: 'string', meta: { interface: 'input', width: 'half', note: 'e.g. 63°F, partly cloudy, 8mph NW' } },
      { field: 'includes_homeschool', type: 'boolean', meta: { interface: 'boolean', width: 'half', note: 'Includes homeschool connections?' } },
      { field: 'divider_observations', type: 'alias', meta: { interface: 'presentation-divider', options: { title: 'Observations' }, special: ['alias', 'no-data'], width: 'full' } },
      { field: 'saw', type: 'json', meta: { interface: 'tags', note: 'Add visual observations one at a time' } },
      { field: 'heard', type: 'text', meta: { interface: 'input-multiline', note: 'Sounds, conversations, wildlife' } },
      { field: 'wondered', type: 'text', meta: { interface: 'input-multiline', note: 'Questions that came up' } },
      { field: 'learned', type: 'text', meta: { interface: 'input-multiline', note: 'Discoveries and answers' } },
    ],
  },
];

// ─── Main ───────────────────────────────────────────────────────────────────

async function main() {
  console.log(`\n🏗️  Setting up Directus schema at ${DIRECTUS_URL}\n`);

  const token = await getToken();
  console.log('✅ Authenticated\n');

  for (const col of COLLECTIONS) {
    console.log(`📦 Creating collection: ${col.name}`);

    // Determine categories for this collection
    const categories = col.name === 'book_reviews' ? BOOK_CATEGORIES : CONTENT_CATEGORIES;

    // Create collection
    try {
      await api(token, 'POST', '/collections', {
        collection: col.name,
        meta: {
          icon: col.icon,
          note: col.note,
          sort_field: 'sort',
          archive_field: 'status',
          archive_value: 'archived',
          unarchive_value: 'draft',
        },
        schema: {},
      });
      console.log(`  ✅ Collection created`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes('already exists')) {
        console.log(`  ⚠ Collection already exists — adding fields`);
      } else {
        console.error(`  ❌ ${msg}`);
        continue;
      }
    }

    // Create fields in order
    const allFields = [
      ...coreFields(col.name, categories),
      ...col.extraFields,
      bodyBlocksField(),
      ...llmFields(),
    ];

    for (const field of allFields) {
      try {
        await api(token, 'POST', `/fields/${col.name}`, field);
        console.log(`  + ${field.field}`);
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        if (msg.includes('already exists')) {
          console.log(`  ⚠ ${field.field} (exists)`);
        } else {
          console.error(`  ❌ ${field.field}: ${msg}`);
        }
      }
    }

    console.log('');
  }

  // Set up public read access for content collections
  console.log('🔒 Setting up access policies...');
  console.log('  ℹ  Content is local-only — no public access configured.');
  console.log('     All access goes through admin credentials.\n');

  console.log('✅ Schema setup complete!\n');
  console.log('Next steps:');
  console.log('  1. Open http://localhost:8055 and verify collections');
  console.log('  2. Run: npx tsx scripts/migrate-content.ts');
  console.log('');
}

main().catch(err => {
  console.error('❌ Setup failed:', err.message);
  process.exit(1);
});
