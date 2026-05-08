import { env } from 'process';

const DIRECTUS_URL = 'http://localhost:8055';
const EMAIL = 'admin@darbees.com';
const PASSWORD = 'localdev';

const PARENT_COLLECTIONS = ['blog_posts', 'book_reviews', 'projects', 'field_notes'];

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

  // 1. Fetch ALL records from content_blocks
  console.log('Fetching all content_blocks records...');
  let offset = 0;
  let allBlocks = [];
  while (true) {
    const res = await api(token, 'GET', `/items/content_blocks?limit=100&offset=${offset}`);
    const data = res.data;
    if (data.length === 0) break;
    allBlocks = allBlocks.concat(data);
    offset += data.length;
  }
  console.log(`Found ${allBlocks.length} block records.`);

  // 2. Delete the bad relations
  console.log('Cleaning up old relations...');
  const relRes = await api(token, 'GET', '/relations');
  for (const rel of relRes.data) {
    if (rel.collection === 'content_blocks' || rel.related_collection === 'content_blocks') {
      try {
        await api(token, 'DELETE', `/relations/${rel.collection}/${rel.field}`);
      } catch(e) {}
    }
  }

  // 3. Drop old content_blocks and aliases
  console.log('Dropping old structures...');
  try { await api(token, 'DELETE', '/collections/content_blocks'); } catch(e) {}
  for (const parent of PARENT_COLLECTIONS) {
    try { await api(token, 'DELETE', `/fields/${parent}/body_blocks`); } catch(e) {}
  }

  // 4. Create dedicated junction tables and relations
  console.log('Creating dedicated junction tables...');
  for (const parent of PARENT_COLLECTIONS) {
    const junction = `${parent}_blocks`;

    // Create junction collection
    try {
      await api(token, 'POST', '/collections', {
        collection: junction,
        meta: { hidden: true },
        schema: {}
      });
    } catch(e) {}

    // Junction fields
    const fields = [
      { field: `${parent}_id`, type: 'integer', schema: { is_nullable: false } },
      { field: 'item', type: 'uuid', schema: { is_nullable: false } },
      { field: 'collection', type: 'string', schema: { is_nullable: false } },
      { field: 'sort', type: 'integer', schema: {} }
    ];
    for (const f of fields) {
      try { await api(token, 'POST', `/fields/${junction}`, f); } catch(e) {}
    }

    // Create parent alias field
    try {
      await api(token, 'POST', `/fields/${parent}`, {
        field: 'body_blocks',
        type: 'alias',
        meta: { interface: 'list-m2a', special: ['m2a'], width: 'full' }
      });
    } catch(e) {}

    // Relation: parent -> junction
    try {
      await api(token, 'POST', '/relations', {
        collection: junction,
        field: `${parent}_id`,
        meta: { one_collection: parent, one_field: 'body_blocks', sort_field: 'sort', one_deselect_action: 'delete' },
        schema: null
      });
    } catch(e) {}

    // Relation: junction -> item
    try {
      await api(token, 'POST', '/relations', {
        collection: junction,
        field: 'item',
        meta: { one_collection_field: 'collection', sort_field: null, one_deselect_action: 'nullify' },
        schema: null
      });
    } catch(e) {}
  }

  // 5. Restore data to new tables
  console.log('Restoring data to dedicated junction tables...');
  for (const block of allBlocks) {
    const parent = block.parent_collection;
    const junction = `${parent}_blocks`;
    try {
      await api(token, 'POST', `/items/${junction}`, {
        [`${parent}_id`]: block.parent_id,
        item: block.item,
        collection: block.collection,
        sort: block.sort
      });
    } catch (e) {
      console.error(`Failed to restore block for ${parent} #${block.parent_id}`);
    }
  }

  console.log('✅ Fix complete!');
}

main().catch(console.error);
