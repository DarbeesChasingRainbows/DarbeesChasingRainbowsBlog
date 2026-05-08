module.exports = ({ action }, { env, services, logger }) => {
  const CONTENT_COLLECTIONS = ['blog_posts', 'book_reviews', 'projects', 'field_notes'];
  
  const GEO_SYSTEM_PROMPT = `You are a Generative Engine Optimization (GEO) specialist for a family blog called "Darbees Chasing Rainbows."

Your job is to analyze blog content and generate structured metadata that makes the content maximally citable and retrievable by AI systems (ChatGPT, Perplexity, Google AI Overviews, etc.).

Return a JSON object with exactly these fields:
{
  "aiSummary": "A 2-3 sentence summary written in third person, naming the entity ('The Darbees'), the topic, and the timeframe. This is the snippet an AI would quote.",
  "keyTakeaways": ["3-5 specific, factual takeaways. Include numbers, names, and concrete details. Each should stand alone as a citable fact."],
  "entityMentions": ["Array of proper nouns, brand names, locations, and key entities mentioned in or relevant to the content. Always include 'The Darbees' and 'Darbees Chasing Rainbows'."],
  "faq": [{"question": "A question a real person would ask about this topic", "answer": "A direct, authoritative answer based on the content. 1-3 sentences."}]
}

Rules:
- Be factual and specific. No filler.
- keyTakeaways should be scannable bullet points with real data.
- faq questions should be things people actually search for.
- entityMentions should include relevant proper nouns from the content.
- aiSummary should be quotable by an AI assistant as-is.`;

  /**
   * Extract plain text from Editor.js blocks and resolved rich media for GEO analysis.
   */
  async function extractText(bodyBlocks, richMedia) {
    const blocks = bodyBlocks?.blocks || [];
    if (blocks.length === 0) return '';

    const textParts = [];
    const usedIndices = {};

    for (const block of blocks) {
      switch (block.type) {
        case 'paragraph': {
          const text = (block.data?.text || '').trim();
          if (!text) break;

          const shortcodeMatch = text.match(/^\[\[([\w_]+)(?::([\w-]+))?\]\]$/);
          if (shortcodeMatch) {
            const [_, typeName, blockId] = shortcodeMatch;
            const blockCol = typeName.startsWith('block_') ? typeName : `block_${typeName.toLowerCase()}`;
            
            let richItem;
            if (blockId) {
              richItem = (richMedia || []).find(rm => 
                rm.collection === blockCol && 
                (String(rm.item?.id || rm.item) === blockId)
              );
            } else {
              const typeCount = usedIndices[blockCol] || 0;
              const candidates = (richMedia || []).filter(rm => rm.collection === blockCol);
              richItem = candidates[typeCount];
              usedIndices[blockCol] = typeCount + 1;
            }

            const data = richItem?.item;
            if (data && typeof data === 'object') {
              const type = blockCol.replace('block_', '');
              if (type === 'callout') textParts.push(`${data.title || ''} ${data.content || ''}`);
              if (type === 'blockquote') textParts.push(`${data.quote || ''} — ${data.author || ''}`);
              if (type === 'accordion') {
                const items = (data.items || []).map(i => `${i.title}: ${i.content}`).join(' ');
                textParts.push(items);
              }
            }
          } else {
            textParts.push(text);
          }
          break;
        }

        case 'header':
          textParts.push(block.data?.text || '');
          break;

        case 'quote':
          textParts.push(`${block.data?.text || ''} ${block.data?.caption || ''}`);
          break;

        case 'list':
          if (block.data?.items) textParts.push(block.data.items.join(' '));
          break;

        case 'table':
          if (block.data?.content) {
            const tableText = block.data.content.map(row => row.join(' ')).join(' ');
            textParts.push(tableText);
          }
          break;
      }
    }

    return textParts.filter(Boolean).join('\n\n');
  }

  action('items.update', async ({ payload, keys, collection }, { schema }) => {
    logger.info(`GEO Optimizer: Detected update on ${collection} [${keys.join(', ')}] with payload: ${JSON.stringify(payload)}`);
    
    // Only process content collections
    if (!CONTENT_COLLECTIONS.includes(collection)) return;

    // Trigger when geo_status is "pending" OR trigger_geo button was clicked
    const isTriggered = payload.geo_status === 'pending' || payload.trigger_geo === true || payload.trigger_geo === 1;
    if (!isTriggered) return;

    logger.info(`GEO Optimizer: Triggered for ${collection}/${keys[0]} (status: ${payload.geo_status}, trigger: ${payload.trigger_geo})`);

    const lmStudioUrl = env.LM_STUDIO_URL || 'http://host.docker.internal:1234/v1';

    try {
      const { ItemsService } = services;
      const itemsService = new ItemsService(collection, {
        schema,
        accountability: { admin: true },
      });

      // Read the full item including rich_media
      const item = await itemsService.readOne(keys[0], {
        fields: ['*', 'rich_media.collection', 'rich_media.item:*.*']
      });
      
      logger.info(`GEO Optimizer: Read item ${item.id}, extracting text...`);
      const bodyText = await extractText(item.body_blocks, item.rich_media);
      logger.info(`GEO Optimizer: Extracted ${bodyText.length} chars of body text.`);

      if (!bodyText || bodyText.length < 50) {
        logger.warn(`GEO Optimizer: Skipping ${collection}/${keys[0]} — body too short (${bodyText.length} chars)`);
        // Reset the trigger button if it was clicked
        if (payload.trigger_geo === true || payload.trigger_geo === 1) {
          await itemsService.updateOne(keys[0], { trigger_geo: false });
        }
        return;
      }

      // Also include title + description for context
      const fullContext = [
        `Title: ${item.title || ''}`,
        `Description: ${item.description || ''}`,
        '',
        bodyText,
      ].join('\n');

      logger.info(`GEO Optimizer: Processing ${collection}/${keys[0]} (${bodyText.length} chars)`);

      const response = await fetch(`${lmStudioUrl}/chat/completions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model: 'local-model',
          messages: [
            { role: 'system', content: GEO_SYSTEM_PROMPT },
            { role: 'user', content: `Analyze this content and generate GEO metadata:\n\n${fullContext}` },
          ],
          temperature: 0.1,
          response_format: { type: 'json_object' },
        }),
      });

      if (!response.ok) {
        const errText = await response.text();
        logger.error(`GEO Optimizer: LM Studio returned ${response.status}: ${errText}`);
        await itemsService.updateOne(keys[0], { geo_status: 'error' });
        return;
      }

      const result = await response.json();
      const content = result.choices?.[0]?.message?.content;

      if (!content) {
        logger.error('GEO Optimizer: Empty response from LM Studio');
        await itemsService.updateOne(keys[0], { geo_status: 'error' });
        return;
      }

      const geoData = JSON.parse(content);

      // Update the item with GEO data
      await itemsService.updateOne(keys[0], {
        ai_summary: geoData.aiSummary || null,
        key_takeaways: geoData.keyTakeaways || [],
        entity_mentions: geoData.entityMentions || [],
        faq: geoData.faq || [],
        geo_status: 'generated',
        trigger_geo: false,
      });

      logger.info(`GEO Optimizer: Successfully generated GEO data for ${collection}/${keys[0]}`);
    } catch (err) {
      logger.error(`GEO Optimizer: Failed for ${collection}/${keys[0]}: ${err.message}`);

      try {
        const { ItemsService } = services;
        const itemsService = new ItemsService(collection, {
          schema,
          accountability: { admin: true },
        });
        await itemsService.updateOne(keys[0], { geo_status: 'error' });
      } catch {
        // Swallow — don't recurse
      }
    }
  });
};
