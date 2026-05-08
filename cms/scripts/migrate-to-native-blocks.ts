import { env } from 'process';

const DIRECTUS_URL = 'http://localhost:8055';
const EMAIL = 'admin@darbees.com';
const PASSWORD = 'localdev';

const PARENT_COLLECTIONS = ['blog_posts', 'book_reviews', 'projects', 'field_notes'];

const NATIVE_BLOCKS = ['block_markdown', 'block_heading', 'block_code', 'block_blockquote'];

async function api(token, method, path, body) {
  const res = await fetch(`${DIRECTUS_URL}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });
  if (!res.ok) {
    const errorText = await res.text();
    // Ignore already exists
    if (errorText.includes('"code":"RECORD_NOT_UNIQUE"')) return;
    if (errorText.includes('"code":"FIELD_ALREADY_EXISTS"')) return;
    throw new Error(`API Error ${res.status} on ${method} ${path}: ${errorText}`);
  }
  return res.status !== 204 ? res.json() : null;
}

async function getToken() {
  const res = await fetch(`${DIRECTUS_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD })
  });
  const data = await res.json();
  return data.data.access_token;
}

async function main() {
  const token = await getToken();
  console.log('✅ Authenticated');

  // 1. Fetch all posts with their M2A data
  console.log('Fetching all posts and their M2A blocks...');
  const inMemoryItems = {};
  for (const parent of PARENT_COLLECTIONS) {
    const res = await api(token, 'GET', `/items/${parent}?fields=id,body_blocks.id,body_blocks.collection,body_blocks.item:*.*&limit=-1`);
    inMemoryItems[parent] = res.data;
  }

  // 2. Delete M2A fields & relations, then recreate
  console.log('Restructuring fields...');
  for (const parent of PARENT_COLLECTIONS) {
    const junction = `${parent}_blocks`;

    // Drop old relation
    try { await api(token, 'DELETE', `/relations/${junction}/${parent}_id`); } catch(e) {}
    // Drop old alias
    try { await api(token, 'DELETE', `/fields/${parent}/body_blocks`); } catch(e) {}

    // Create new JSON body_blocks
    try {
      await api(token, 'POST', `/fields/${parent}`, {
        field: 'body_blocks',
        type: 'json',
        meta: { interface: 'blocks', width: 'full' }
      });
    } catch(e) {}

    // Create rich_media M2A alias
    try {
      await api(token, 'POST', `/fields/${parent}`, {
        field: 'rich_media',
        type: 'alias',
        meta: { interface: 'list-m2a', special: ['m2a'], width: 'full' }
      });
    } catch(e) {}

    // Recreate relation pointing to rich_media
    try {
      await api(token, 'POST', '/relations', {
        collection: junction,
        field: `${parent}_id`,
        related_collection: parent,
        meta: { one_collection: parent, one_field: 'rich_media', sort_field: 'sort', one_deselect_action: 'delete', junction_field: 'item' },
        schema: null
      });
    } catch(e) {}
  }

  // 3. Process and Migrate Data
  console.log('Converting data to Native Blocks...');
  for (const parent of PARENT_COLLECTIONS) {
    const items = inMemoryItems[parent];
    if (!items) continue;
    const junction = `${parent}_blocks`;

    for (const item of items) {
      if (!item.body_blocks || item.body_blocks.length === 0) continue;

      const editorBlocks = [];
      const blocksToDelete = [];

      for (const m2a of item.body_blocks) {
        if (!m2a || !m2a.collection || !m2a.item) continue;
        
        const col = m2a.collection;
        const data = m2a.item;

        if (NATIVE_BLOCKS.includes(col)) {
          // Map to Editor.js Native Block
          if (col === 'block_markdown') {
            editorBlocks.push({ type: 'paragraph', data: { text: data.content || '' } });
          } else if (col === 'block_heading') {
            editorBlocks.push({ type: 'header', data: { text: data.text || '', level: data.level || 2 } });
          } else if (col === 'block_code') {
            // Directus code block uses 'code' property
            editorBlocks.push({ type: 'code', data: { code: data.content || '', language: data.language || '' } });
          } else if (col === 'block_blockquote') {
            const caption = [data.author, data.context].filter(Boolean).join(', ');
            editorBlocks.push({ type: 'quote', data: { text: data.quote || '', caption, alignment: 'left' } });
          }
          // Mark for deletion
          blocksToDelete.push({ junctionId: m2a.id, collection: col, itemId: data.id });
        } else {
          // Rich Media: leave in M2A, insert shortcode
          editorBlocks.push({ type: 'paragraph', data: { text: `[[${col}:${data.id}]]` } });
        }
      }

      const editorPayload = {
        time: Date.now(),
        blocks: editorBlocks,
        version: "2.22.2"
      };

      // Patch the parent item
      try {
        await api(token, 'PATCH', `/items/${parent}/${item.id}`, {
          body_blocks: editorPayload
        });
        
        // Clean up native blocks from database
        for (const del of blocksToDelete) {
          try { await api(token, 'DELETE', `/items/${junction}/${del.junctionId}`); } catch(e) {}
          try { await api(token, 'DELETE', `/items/${del.collection}/${del.itemId}`); } catch(e) {}
        }
      } catch (e) {
        console.error(`Failed to migrate item ${parent}/${item.id}`, e);
      }
    }
  }

  // 4. Drop native block collections
  console.log('Dropping native block collections...');
  for (const col of NATIVE_BLOCKS) {
    try { await api(token, 'DELETE', `/collections/${col}`); } catch(e) {}
  }

  // Also patch the item relation to only allow rich media components
  console.log('Updating allowed collections on rich_media...');
  const RICH_MEDIA = ['block_callout','block_carousel','block_gallery','block_video','block_cta','block_accordion'].join(',');
  for (const parent of PARENT_COLLECTIONS) {
    try {
      await api(token, 'PATCH', `/relations/${parent}_blocks/item`, {
        meta: { one_allowed_collections: RICH_MEDIA }
      });
    } catch(e) {}
  }

  console.log('✅ Option A Migration complete!');
}

main().catch(console.error);
