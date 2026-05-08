/**
 * MDX Export Hook — Directus Extension
 *
 * Fires when a content item's `status` changes to `published`.
 * Assembles `body_blocks` into MDX with proper component imports,
 * serializes frontmatter as YAML, and writes the .mdx file to
 * the Astro content directory.
 */

const { readFileSync, writeFileSync, mkdirSync, copyFileSync, existsSync, appendFileSync } = require('fs');
const { join, basename } = require('path');

// ─── Collection → Astro folder mapping ─────────────────────────────────────

const COLLECTION_MAP = {
  blog_posts: {
    astroFolder: 'blog',
    importPrefix: '../../components',
    fields: {
      title: 'title',
      description: 'description',
      pub_date: 'pubDate',
      updated_date: 'updatedDate',
      author: 'author',
      category: 'category',
      tags: 'tags',
      image_alt: 'imageAlt',
      image_attribution_name: { nested: 'imageAttribution', key: 'name' },
      image_attribution_url: { nested: 'imageAttribution', key: 'url' },
      preview: 'preview',
      ai_summary: 'aiSummary',
      key_takeaways: 'keyTakeaways',
      entity_mentions: 'entityMentions',
      faq: 'faq',
      sources: 'sources',
    },
  },
  book_reviews: {
    astroFolder: 'books',
    importPrefix: '../../components',
    fields: {
      title: 'title',
      description: 'description',
      pub_date: 'pubDate',
      updated_date: 'updatedDate',
      book_title: 'bookTitle',
      book_author: 'author',
      category: 'category',
      tags: 'tags',
      age_range: 'ageRange',
      format_used: 'formatUsed',
      rating: 'rating',
      dad_take: 'dadTake',
      mom_take: 'momTake',
      kids_take: 'kidsTake',
      read_aloud_value: 'readAloudValue',
      audiobook_value: 'audiobookValue',
      educational_value: 'educationalValue',
      content_notes: 'contentNotes',
      worldview_notes: 'worldviewNotes',
      image_alt: 'imageAlt',
      preview: 'preview',
      ai_summary: 'aiSummary',
      key_takeaways: 'keyTakeaways',
      entity_mentions: 'entityMentions',
      faq: 'faq',
      sources: 'sources',
    },
  },
  projects: {
    astroFolder: 'projects',
    importPrefix: '../../components',
    fields: {
      title: 'title',
      description: 'description',
      pub_date: 'pubDate',
      updated_date: 'updatedDate',
      category: 'category',
      tags: 'tags',
      difficulty: 'difficulty',
      estimated_cost: 'estimatedCost',
      estimated_time: 'estimatedTime',
      github_url: 'githubUrl',
      parts_list: 'partsList',
      image_alt: 'imageAlt',
      preview: 'preview',
      ai_summary: 'aiSummary',
      key_takeaways: 'keyTakeaways',
      entity_mentions: 'entityMentions',
      faq: 'faq',
      sources: 'sources',
    },
  },
  field_notes: {
    astroFolder: 'field-notes',
    importPrefix: '../../components',
    fields: {
      title: 'title',
      description: 'description',
      pub_date: 'pubDate',
      location: 'location',
      region: 'region',
      weather: 'weather',
      category: 'category',
      tags: 'tags',
      includes_homeschool: 'includesHomeschool',
      saw: 'saw',
      heard: 'heard',
      wondered: 'wondered',
      learned: 'learned',
      image_alt: 'imageAlt',
      preview: 'preview',
      ai_summary: 'aiSummary',
      key_takeaways: 'keyTakeaways',
      entity_mentions: 'entityMentions',
      faq: 'faq',
      sources: 'sources',
    },
  },
};

// ─── YAML Serialization ─────────────────────────────────────────────────────

/**
 * Serialize a value to YAML format with proper indentation.
 */
function yamlValue(value, indent = 0) {
  if (value === null || value === undefined) return null;
  if (typeof value === 'boolean') return value.toString();
  if (typeof value === 'number') return value.toString();

  if (typeof value === 'string') {
    // Multi-line strings use >- block scalar
    if (value.includes('\n') || value.length > 120) {
      const indentStr = '  '.repeat(indent + 1);
      const lines = value.split('\n').map(l => `${indentStr}${l}`);
      return `>-\n${lines.join('\n')}`;
    }
    // Strings that need quoting
    if (value.includes(':') || value.includes('#') || value.includes("'") ||
        value.includes('"') || value.startsWith('!') || value.startsWith('&') ||
        value.startsWith('*') || /^\d{4}-\d{2}-\d{2}/.test(value)) {
      return `'${value.replace(/'/g, "''")}'`;
    }
    return value;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return '[]';

    // Simple string/number arrays → inline format for short arrays
    if (value.every(v => typeof v === 'string' || typeof v === 'number')) {
      const inline = `[${value.map(v => typeof v === 'string' ? `'${v.replace(/'/g, "''")}'` : v).join(', ')}]`;
      if (inline.length < 100) return inline;
    }

    // Array of objects or long arrays → block format
    const indentStr = '  '.repeat(indent);
    return '\n' + value.map(item => {
      if (typeof item === 'object' && item !== null && !Array.isArray(item)) {
        const entries = Object.entries(item).filter(([, v]) => v !== null && v !== undefined);
        const first = entries[0];
        const rest = entries.slice(1);
        let line = `${indentStr}  - ${first[0]}: ${yamlValue(first[1], indent + 2)}`;
        for (const [k, v] of rest) {
          line += `\n${indentStr}    ${k}: ${yamlValue(v, indent + 2)}`;
        }
        return line;
      }
      return `${indentStr}  - ${yamlValue(item, indent + 1)}`;
    }).join('\n');
  }

  if (typeof value === 'object') {
    const indentStr = '  '.repeat(indent);
    const entries = Object.entries(value).filter(([, v]) => v !== null && v !== undefined);
    if (entries.length === 0) return null;
    return '\n' + entries.map(([k, v]) => {
      const serialized = yamlValue(v, indent + 1);
      if (serialized === null) return null;
      return `${indentStr}  ${k}: ${serialized}`;
    }).filter(Boolean).join('\n');
  }

  return String(value);
}

/**
 * Serialize frontmatter object to YAML string.
 */
function serializeFrontmatter(data) {
  const lines = [];

  for (const [key, value] of Object.entries(data)) {
    if (value === null || value === undefined) continue;
    if (typeof value === 'string' && value === '') continue;
    if (Array.isArray(value) && value.length === 0) continue;

    const serialized = yamlValue(value, 0);
    if (serialized === null) continue;

    lines.push(`${key}: ${serialized}`);
  }

  return `---\n${lines.join('\n')}\n---`;
}

// ─── Block → MDX Assembly ───────────────────────────────────────────────────

/**
 * Process a single rich media block (M2A item) into MDX.
 */
async function processRichBlock(block, type, imports, importPrefix, collection, slug, exportPath, services, schema) {
  switch (type) {
    case 'callout': {
      imports.add(`import Callout from '${importPrefix}/Callout.astro';`);
      const variant = block.type_variant || 'note';
      const typeAttr = variant !== 'note' ? ` type="${variant}"` : '';
      const titleAttr = block.title ? ` title="${block.title}"` : '';
      return `<Callout${typeAttr}${titleAttr}>\n${(block.content || '').trim()}\n</Callout>`;
    }

    case 'table': {
      if (!block.headers?.length) return '';
      const headerRow = `| ${block.headers.join(' | ')} |`;
      const sepRow = `| ${block.headers.map(() => '---').join(' | ')} |`;
      const dataRows = (block.rows || []).map(row => {
        const cells = Array.isArray(row) ? row : Object.values(row);
        return `| ${cells.join(' | ')} |`;
      });
      return [headerRow, sepRow, ...dataRows].join('\n');
    }

    case 'image': {
      const alt = block.alt || '';
      let src = '';
      if (block.file) {
        src = await resolveImage(block.file, collection, slug, exportPath, services, schema) || '';
      } else if (block.resolved_path) {
        src = block.resolved_path;
      } else if (block.filename) {
        src = `./images/${block.filename}`;
      }
      if (!src) return '';
      const imgTag = `![${alt}](${src})`;
      return block.caption ? `${imgTag}\n*${block.caption}*` : imgTag;
    }

    case 'carousel': {
      if (!block.slides || block.slides.length === 0) return '';
      imports.add(`import Carousel from '${importPrefix}/Carousel.astro';`);
      const slidesData = [];
      for (const slide of block.slides) {
        let src = '';
        if (slide.file) {
          src = await resolveImage(slide.file, collection, slug, exportPath, services, schema) || '';
        }
        if (src) {
          slidesData.push(`  { src: '${src}', alt: '${(slide.alt || '').replace(/'/g, "\\'")}', caption: '${(slide.caption || '').replace(/'/g, "\\'")}' }`);
        }
      }
      if (slidesData.length === 0) return '';
      return `<Carousel slides={[\n${slidesData.join(',\n')}\n]} />`;
    }

    case 'gallery': {
      if (!block.images || block.images.length === 0) return '';
      imports.add(`import ImageGallery from '${importPrefix}/ImageGallery.astro';`);
      const cols = block.columns || 3;
      const imagesData = [];
      for (const img of block.images) {
        let src = '';
        if (img.file) {
          src = await resolveImage(img.file, collection, slug, exportPath, services, schema) || '';
        }
        if (src) {
          imagesData.push(`  { src: '${src}', alt: '${(img.alt || '').replace(/'/g, "\\'")}', caption: '${(img.caption || '').replace(/'/g, "\\'")}' }`);
        }
      }
      if (imagesData.length === 0) return '';
      return `<ImageGallery columns={${cols}} images={[\n${imagesData.join(',\n')}\n]} />`;
    }

    case 'video': {
      if (!block.url) return '';
      imports.add(`import VideoEmbed from '${importPrefix}/VideoEmbed.astro';`);
      return `<VideoEmbed url="${block.url}" caption="${(block.caption || '').replace(/"/g, '&quot;')}" />`;
    }

    case 'cta': {
      if (!block.url || !block.text) return '';
      imports.add(`import CTA from '${importPrefix}/CTA.astro';`);
      const style = block.style || 'primary';
      return `<CTA url="${block.url}" style="${style}">${block.text}</CTA>`;
    }

    case 'accordion': {
      if (!block.items || block.items.length === 0) return '';
      imports.add(`import Accordion from '${importPrefix}/Accordion.astro';`);
      const itemsData = block.items.map(i => `  { title: '${(i.title || '').replace(/'/g, "\\'")}', content: \`${(i.content || '').replace(/`/g, '\\`')}\` }`);
      return `<Accordion items={[\n${itemsData.join(',\n')}\n]} />`;
    }

    default:
      return `{/* Unknown rich media type: ${type} */}`;
  }
}

/**
 * Assemble body_blocks (Editor.js) and rich_media (M2A) into MDX.
 */
async function assembleBlocks(bodyBlocks, richMedia, importPrefix, collection, slug, exportPath, services, schema, logger) {
  const blocks = (bodyBlocks && bodyBlocks.blocks) || [];
  if (blocks.length === 0) return '';

  const imports = new Set();
  const mdxParts = [];
  const usedIndices = {}; // Track usage of rich media items for blind shortcodes

  for (const block of blocks) {
    switch (block.type) {
      case 'paragraph': {
        const text = (block.data && block.data.text || '').trim();
        if (!text) break;

        // Check for shortcode [[Type]] or [[block_type:id]]
        // Regex matches [[Carousel]] or [[block_carousel:123]]
        const shortcodeMatch = text.match(/^\[\[([\w_]+)(?::([\w-]+))?\]\]$/);
        
        if (shortcodeMatch) {
          const [_, typeName, blockId] = shortcodeMatch;
          const blockCol = typeName.startsWith('block_') ? typeName : `block_${typeName.toLowerCase()}`;
          
          let richItem;
          if (blockId) {
            // Explicit ID lookup
            richItem = (richMedia || []).find(rm => 
              rm.collection === blockCol && 
              (String(rm.item?.id || rm.item) === blockId)
            );
          } else {
            // Blind lookup: take the next unused item of this type from the rich_media list
            const typeCount = usedIndices[blockCol] || 0;
            const candidates = (richMedia || []).filter(rm => rm.collection === blockCol);
            richItem = candidates[typeCount];
            usedIndices[blockCol] = typeCount + 1;
          }

          if (richItem && richItem.item) {
            const itemData = typeof richItem.item === 'object' ? richItem.item : { id: richItem.item };
            const content = await processRichBlock(itemData, blockCol.replace('block_', ''), imports, importPrefix, collection, slug, exportPath, services, schema);
            if (content) mdxParts.push(content);
          } else {
            mdxParts.push(`{/* Shortcode target not found: ${typeName}${blockId ? ':' + blockId : ''} */}`);
          }
        } else {
          // Normal paragraph
          mdxParts.push(text);
        }
        break;
      }

      case 'header': {
        const prefix = '#'.repeat((block.data && block.data.level) || 2);
        mdxParts.push(`${prefix} ${(block.data && block.data.text) || ''}`);
        break;
      }

      case 'quote': {
        imports.add(`import Blockquote from '${importPrefix}/Blockquote.astro';`);
        const text = (block.data && block.data.text || '').trim();
        const caption = (block.data && block.data.caption || '').trim();
        const captionAttr = caption ? ` author="${caption.replace(/"/g, '&quot;')}"` : '';
        mdxParts.push(`<Blockquote${captionAttr}>\n${text}\n</Blockquote>`);
        break;
      }

      case 'code': {
        const lang = (block.data && block.data.language) || '';
        const code = (block.data && block.data.code) || '';
        mdxParts.push('```' + lang + '\n' + code + '\n```');
        break;
      }

      case 'list': {
        const items = (block.data && block.data.items) || [];
        const isOrdered = block.data && block.data.style === 'ordered';
        const listMdx = items.map((item, i) => {
          const prefix = isOrdered ? `${i + 1}.` : '-';
          return `${prefix} ${item}`;
        }).join('\n');
        mdxParts.push(listMdx);
        break;
      }

      case 'table': {
        const content = (block.data && block.data.content) || [];
        if (content.length === 0) break;
        const rows = content.map(row => `| ${row.join(' | ')} |`);
        // Use first row as header if it's a native table block
        const header = rows[0];
        const sep = `| ${content[0].map(() => '---').join(' | ')} |`;
        mdxParts.push([header, sep, ...rows.slice(1)].join('\n'));
        break;
      }

      default:
        mdxParts.push(`{/* Unsupported block type: ${block.type} */}`);
    }
  }

  // Build final MDX: imports first, then body
  const importLines = Array.from(imports).sort().join('\n');
  const body = mdxParts.join('\n\n');

  return importLines ? `${importLines}\n\n${body}` : body;
}

// ─── Image Resolution ───────────────────────────────────────────────────────

/**
 * Resolve a Directus file ID to a local path, copying the file if needed.
 */
async function resolveImage(fileId, collection, slug, exportPath, services, schema) {
  if (!fileId) return null;

  try {
    const { FilesService } = services;
    const filesService = new FilesService({ schema, accountability: { admin: true } });
    const file = await filesService.readOne(fileId);

    if (!file || !file.filename_disk) return null;

    const astroFolder = COLLECTION_MAP[collection]?.astroFolder || collection;
    const imagesDir = join(exportPath, astroFolder, 'images');

    // Ensure images directory exists
    if (!existsSync(imagesDir)) {
      mkdirSync(imagesDir, { recursive: true });
    }

    const ext = (file.filename_download && file.filename_download.split('.').pop()) || 'jpg';
    const destFilename = `${slug}-${file.title || file.id}.${ext}`.replace(/[^a-z0-9._-]/gi, '-').toLowerCase();
    const destPath = join(imagesDir, destFilename);
    const uploadsPath = join('/directus/uploads', file.filename_disk);

    if (existsSync(uploadsPath)) {
      copyFileSync(uploadsPath, destPath);
    }

    // Return relative path for frontmatter
    return `./images/${destFilename}`;
  } catch (err) {
    return null;
  }
}

// ─── Export Single Item ─────────────────────────────────────────────────────

/**
 * Export a single Directus item as an MDX file.
 */
async function exportItem(item, collection, exportPath, services, schema, logger) {
  const config = COLLECTION_MAP[collection];
  if (!config) {
    logger?.warn(`MDX Export: Unknown collection "${collection}"`);
    return false;
  }

  const slug = item.slug;
  if (!slug) {
    logger?.warn(`MDX Export: Item ${item.id} in ${collection} has no slug`);
    return false;
  }

  // Build frontmatter from field mapping
  const frontmatter = {};
  const nestedObjects = {};

  for (const [directusField, mapping] of Object.entries(config.fields)) {
    const value = item[directusField];
    if (value === null || value === undefined) continue;
    if (typeof value === 'string' && value === '') continue;

    if (typeof mapping === 'object' && mapping.nested) {
      // Accumulate nested object fields
      if (!nestedObjects[mapping.nested]) nestedObjects[mapping.nested] = {};
      nestedObjects[mapping.nested][mapping.key] = value;
    } else {
      frontmatter[mapping] = value;
    }
  }

  // Merge nested objects into frontmatter
  for (const [key, obj] of Object.entries(nestedObjects)) {
    const nonEmpty = Object.entries(obj).filter(([, v]) => v !== null && v !== undefined && v !== '');
    if (nonEmpty.length > 0) {
      frontmatter[key] = Object.fromEntries(nonEmpty);
    }
  }

  // Handle draft status
  frontmatter.draft = item.status === 'draft';

  // Handle date formatting
  if (frontmatter.pubDate instanceof Date) {
    frontmatter.pubDate = frontmatter.pubDate.toISOString().split('T')[0];
  }
  if (frontmatter.updatedDate instanceof Date) {
    frontmatter.updatedDate = frontmatter.updatedDate.toISOString().split('T')[0];
  }

  // Resolve hero image
  if (item.hero_image) {
    const heroPath = await resolveImage(item.hero_image, collection, slug, exportPath, services, schema);
    if (heroPath) frontmatter.heroImage = heroPath;
  }

  // Resolve featured image
  if (item.featured_image) {
    const featuredPath = await resolveImage(item.featured_image, collection, slug, exportPath, services, schema);
    if (featuredPath) frontmatter.featuredImage = featuredPath;
  }

  // Assemble body blocks into MDX
  const body = await assembleBlocks(item.body_blocks, item.rich_media, config.importPrefix, collection, slug, exportPath, services, schema, logger);

  // Serialize
  const yamlFrontmatter = serializeFrontmatter(frontmatter);
  const mdxContent = `${yamlFrontmatter}\n\n${body}\n`;

  // Write file
  const outputDir = join(exportPath, config.astroFolder);
  if (!existsSync(outputDir)) {
    mkdirSync(outputDir, { recursive: true });
  }

  try {
    appendFileSync('/directus/astro-content/hook.log', `[${new Date().toISOString()}] MDX generated:\n${mdxContent}\n`);
  } catch (e) {}

  const outputPath = join(outputDir, `${slug}.mdx`);
  writeFileSync(outputPath, mdxContent, 'utf-8');

  logger?.info(`MDX Export: Wrote ${outputPath}`);
  return true;
}

// ─── Hook Registration ──────────────────────────────────────────────────────

module.exports = ({ action }, { env, services, logger }) => {
  logger?.info("MDX Export Hook: LOADING...");
  const exportPath = env.CONTENT_EXPORT_PATH || '/directus/astro-content';
  const collections = Object.keys(COLLECTION_MAP);

  action('items.update', async ({ payload, keys, collection }, { schema }) => {
    if (!collections.includes(collection)) return;
    
    try {
      appendFileSync('/directus/astro-content/hook.log', `[${new Date().toISOString()}] items.update ${collection} ${keys.join(',')}\n`);
    } catch (e) {}

    try {
      const { ItemsService } = services;
      const itemsService = new ItemsService(collection, {
        schema,
        accountability: { admin: true },
      });

      const query = {
        fields: ['*', 'rich_media.collection', 'rich_media.item:*.*']
      };
      const item = await itemsService.readOne(keys[0], query);

      // Export if status is changing to published, or if it's already published
      const isPublished = payload.status === 'published' || (payload.status === undefined && item.status === 'published');
      if (!isPublished) return;

      // Merge payload into item so exportItem gets the latest data
      const mergedItem = Object.assign({}, item, payload);
      
      await exportItem(mergedItem, collection, exportPath, services, schema, logger);
    } catch (err) {
      logger.error(`MDX Export: Failed for ${collection}/${keys[0]}: ${err.message}`);
    }
  });

  // Also export on create if status is already "published"
  action('items.create', async ({ payload, key, collection }, { schema }) => {
    if (!collections.includes(collection)) return;
    if (payload.status !== 'published') return;

    try {
      const { ItemsService } = services;
      const itemsService = new ItemsService(collection, {
        schema,
        accountability: { admin: true },
      });

      const query = {
        fields: ['*', 'rich_media.collection', 'rich_media.item:*.*']
      };
      const item = await itemsService.readOne(key, query);
      await exportItem(item, collection, exportPath, services, schema, logger);
    } catch (err) {
      logger.error(`MDX Export: Failed for ${collection}/${key}: ${err.message}`);
    }
  });
};
