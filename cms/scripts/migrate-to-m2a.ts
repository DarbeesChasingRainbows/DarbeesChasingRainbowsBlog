import { env } from 'process';

const DIRECTUS_URL = 'http://localhost:8055';
const EMAIL = 'admin@darbees.com';
const PASSWORD = 'localdev';

const PARENT_COLLECTIONS = ['blog_posts', 'book_reviews', 'projects', 'field_notes'];

const BLOCK_COLLECTIONS = [
  {
    name: 'block_markdown',
    fields: [
      { field: 'content', type: 'text', meta: { interface: 'input-multiline', width: 'full' } }
    ]
  },
  {
    name: 'block_heading',
    fields: [
      { field: 'level', type: 'integer', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{text:'H2',value:2}, {text:'H3',value:3}, {text:'H4',value:4}] }, default_value: 2 } },
      { field: 'text', type: 'string', meta: { interface: 'input', width: 'full' } }
    ]
  },
  {
    name: 'block_callout',
    fields: [
      { field: 'type_variant', type: 'string', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{text:'Note',value:'note'}, {text:'Tip',value:'tip'}, {text:'Warning',value:'warning'}, {text:'Faith',value:'faith'}, {text:'Project',value:'project'}] }, default_value: 'note' } },
      { field: 'title', type: 'string', meta: { interface: 'input', width: 'half' } },
      { field: 'content', type: 'text', meta: { interface: 'input-multiline', width: 'full' } }
    ]
  },
  {
    name: 'block_code',
    fields: [
      { field: 'language', type: 'string', meta: { interface: 'input', width: 'half' } },
      { field: 'content', type: 'text', meta: { interface: 'input-code', width: 'full' } }
    ]
  },
  {
    name: 'block_carousel',
    fields: [
      { field: 'slides', type: 'json', meta: { interface: 'list', options: { template: '{{alt}}', fields: [ { field: 'file', type: 'uuid', meta: { interface: 'file-image', required: true } }, { field: 'alt', type: 'string', meta: { interface: 'input' } }, { field: 'caption', type: 'string', meta: { interface: 'input' } } ] } } }
    ]
  },
  {
    name: 'block_gallery',
    fields: [
      { field: 'columns', type: 'integer', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{text:'2 Columns',value:2},{text:'3 Columns',value:3},{text:'4 Columns',value:4}] }, default_value: 3 } },
      { field: 'images', type: 'json', meta: { interface: 'list', options: { template: '{{alt}}', fields: [ { field: 'file', type: 'uuid', meta: { interface: 'file-image', required: true } }, { field: 'alt', type: 'string', meta: { interface: 'input' } }, { field: 'caption', type: 'string', meta: { interface: 'input' } } ] } } }
    ]
  },
  {
    name: 'block_video',
    fields: [
      { field: 'url', type: 'string', meta: { interface: 'input' } }
    ]
  },
  {
    name: 'block_cta',
    fields: [
      { field: 'url', type: 'string', meta: { interface: 'input' } },
      { field: 'text', type: 'string', meta: { interface: 'input', width: 'half' } },
      { field: 'style', type: 'string', meta: { interface: 'select-dropdown', width: 'half', options: { choices: [{text:'Primary',value:'primary'},{text:'Secondary',value:'secondary'},{text:'Accent',value:'accent'},{text:'Outline',value:'outline'}] }, default_value: 'primary' } }
    ]
  },
  {
    name: 'block_blockquote',
    fields: [
      { field: 'quote', type: 'text', meta: { interface: 'input-multiline', width: 'full' } },
      { field: 'author', type: 'string', meta: { interface: 'input', width: 'half' } },
      { field: 'context', type: 'string', meta: { interface: 'input', width: 'half' } }
    ]
  },
  {
    name: 'block_accordion',
    fields: [
      { field: 'items', type: 'json', meta: { interface: 'list', options: { template: '{{title}}', fields: [ { field: 'title', type: 'string', meta: { interface: 'input', required: true, width: 'full' } }, { field: 'content', type: 'text', meta: { interface: 'input-multiline', required: true, width: 'full' } } ] } } }
    ]
  }
];

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

  // 1. Fetch all items with their legacy JSON body_blocks
  const inMemoryItems = {};
  for (const collection of PARENT_COLLECTIONS) {
    console.log(`Fetching items for ${collection}...`);
    const res = await api(token, 'GET', `/items/${collection}?limit=-1`);
    inMemoryItems[collection] = res.data;
  }

  // 2. Drop the old body_blocks fields so we can reuse the name 'body_blocks' for the M2A alias
  console.log('Dropping old body_blocks fields...');
  for (const collection of PARENT_COLLECTIONS) {
    try {
      await api(token, 'DELETE', `/fields/${collection}/body_blocks`);
      console.log(`  Dropped body_blocks from ${collection}`);
    } catch (e) {
      console.log(`  No body_blocks to drop from ${collection}`);
    }
  }

  // 3. Create content_blocks junction collection
  console.log('Creating content_blocks junction collection...');
  try {
    await api(token, 'POST', '/collections', {
      collection: 'content_blocks',
      meta: { hidden: true },
      schema: {}
    });
  } catch(e) {}
  
  // Junction fields
  const junctionFields = [
    { field: 'parent_collection', type: 'string', schema: { is_nullable: false } },
    { field: 'parent_id', type: 'integer', schema: { is_nullable: false } },
    { field: 'item', type: 'uuid', schema: { is_nullable: false } },
    { field: 'collection', type: 'string', schema: { is_nullable: false } },
    { field: 'sort', type: 'integer', schema: {} }
  ];
  for (const f of junctionFields) {
    try { await api(token, 'POST', '/fields/content_blocks', f); } catch(e) {}
  }

  // 4. Create block collections
  console.log('Creating block collections...');
  for (const block of BLOCK_COLLECTIONS) {
    try {
      await api(token, 'POST', '/collections', {
        collection: block.name,
        meta: { hidden: true },
        schema: {}
      });
    } catch(e) {}

    for (const field of block.fields) {
      try { await api(token, 'POST', `/fields/${block.name}`, field); } catch(e) {}
    }
  }

  // 5. Setup M2A relations for each parent collection
  console.log('Setting up M2A relations...');
  for (const collection of PARENT_COLLECTIONS) {
    // Add the M2A alias field to parent
    try {
      await api(token, 'POST', `/fields/${collection}`, {
        field: 'body_blocks',
        type: 'alias',
        meta: {
          interface: 'list-m2a',
          special: ['m2a'],
          width: 'full'
        }
      });
      console.log(`  Created body_blocks M2A alias on ${collection}`);
    } catch(e) {}

    // Add relation
    try {
      await api(token, 'POST', `/relations`, {
        collection: 'content_blocks',
        field: 'item',
        meta: {
          one_collection_field: 'collection',
          sort_field: null,
          one_deselect_action: 'nullify',
          junction_field: 'parent_id'
        },
        schema: null
      });
    } catch(e) {}

    try {
      await api(token, 'POST', `/relations`, {
        collection: 'content_blocks',
        field: 'parent_id',
        meta: {
          one_collection: collection,
          one_field: 'body_blocks',
          sort_field: 'sort',
          one_deselect_action: 'delete'
        },
        schema: null
      });
    } catch(e) {}
  }

  // 6. Migrate existing data
  console.log('Migrating existing blocks to M2A records...');
  for (const collection of PARENT_COLLECTIONS) {
    const items = inMemoryItems[collection];
    if (!items) continue;

    for (const item of items) {
      if (!item.body_blocks || !Array.isArray(item.body_blocks) || item.body_blocks.length === 0) continue;
      
      console.log(`  Migrating ${collection} #${item.id} (${item.body_blocks.length} blocks)...`);
      
      let sort = 1;
      for (const block of item.body_blocks) {
        if (!block.type) continue;
        
        let blockCollection = `block_${block.type}`;
        // Map any old legacy types if needed
        if (block.type === 'callout' || block.type === 'heading' || block.type === 'markdown' || block.type === 'code' || block.type === 'carousel' || block.type === 'gallery' || block.type === 'video' || block.type === 'cta' || block.type === 'blockquote' || block.type === 'accordion') {
          // It's valid
        } else {
          continue; // Skip invalid or old field_notes_block
        }

        // Insert into the actual block collection
        const blockData = { ...block };
        delete blockData.type; // not needed in the block table itself since the collection name defines it
        
        try {
          const insertedBlock = await api(token, 'POST', `/items/${blockCollection}`, blockData);
          const blockId = insertedBlock.data.id;

          // Insert junction record
          await api(token, 'POST', `/items/content_blocks`, {
            parent_collection: collection,
            parent_id: item.id,
            item: blockId,
            collection: blockCollection,
            sort: sort++
          });
        } catch(e) {
          console.error(`Failed to migrate block for ${collection} #${item.id}:`, e);
        }
      }
    }
  }

  console.log('✅ Migration complete!');
}

main().catch(console.error);
