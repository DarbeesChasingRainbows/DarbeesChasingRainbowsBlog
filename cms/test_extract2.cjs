const { assembleBlocks } = require('./test_assemble2.js');

async function test() {
  const loginRes = await fetch('http://localhost:8055/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'admin@darbees.com', password: 'localdev' })
  });
  const auth = await loginRes.json();
  const token = auth.data.access_token;
  
  const res = await fetch('http://localhost:8055/items/blog_posts/1?fields=*,rich_media.collection,rich_media.item.*', {
    headers: { Authorization: 'Bearer ' + token }
  });
  const { data: item } = await res.json();
  
  try {
    const mdx = await assembleBlocks(
      item.body_blocks,
      item.rich_media,
      '../../components',
      'blog_posts',
      item.slug,
      '/tmp',
      {},
      {}
    );
    console.log('--- OUTPUT ---');
    console.log(mdx.slice(-1000));
  } catch(e) {
    console.error('ERROR:', e);
  }
}
test();
