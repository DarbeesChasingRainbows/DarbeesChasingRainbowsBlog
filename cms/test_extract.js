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
  
  const blocks = item.body_blocks?.blocks || [];
  const richMedia = item.rich_media || [];
  const usedIndices = {};
  const mdxParts = [];
  
  for (const block of blocks) {
    if (block.type === 'paragraph') {
      const text = (block.data?.text || '').trim();
      const shortcodeMatch = text.match(/^\[\[([\w_]+)(?::([\w-]+))?\]\]$/);
      if (shortcodeMatch) {
        const [_, typeName, blockId] = shortcodeMatch;
        const blockCol = typeName.startsWith('block_') ? typeName : `block_${typeName.toLowerCase()}`;
        let richItem;
        if (blockId) {
          richItem = richMedia.find(rm => rm.collection === blockCol && String(rm.item?.id || rm.item) === blockId);
        } else {
          const typeCount = usedIndices[blockCol] || 0;
          const candidates = richMedia.filter(rm => rm.collection === blockCol);
          richItem = candidates[typeCount];
          usedIndices[blockCol] = typeCount + 1;
        }
        
        if (richItem && richItem.item) {
          // Mock processRichBlock
          const itemData = typeof richItem.item === 'object' ? richItem.item : { id: richItem.item };
          const variant = itemData.type_variant || 'note';
          const typeAttr = variant !== 'note' ? ` type="${variant}"` : '';
          const titleAttr = itemData.title ? ` title="${itemData.title}"` : '';
          const content = `<Callout${typeAttr}${titleAttr}>\n${(itemData.content || '').trim()}\n</Callout>`;
          mdxParts.push(content);
          console.log('PUSHED CALLOUT:', content);
        } else {
          mdxParts.push(`{/* Shortcode target not found: ${typeName} */}`);
        }
      } else {
        mdxParts.push(text);
      }
    }
  }
  
  console.log('--- OUTPUT ---');
  console.log(mdxParts.slice(-3).join('\n\n'));
}
test();
