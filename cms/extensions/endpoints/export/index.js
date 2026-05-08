/**
 * Manual Export Endpoint — Directus Extension
 *
 * Provides a REST API for manually triggering MDX export:
 *   GET /custom/export/all                   → export all published items
 *   GET /custom/export/:collection           → export all published items in a collection
 *   GET /custom/export/:collection/:slug     → export a single item (any status)
 */

import { exportItem } from '../hooks/mdx-export/index.js';

const COLLECTIONS = ['blog_posts', 'book_reviews', 'projects', 'field_notes'];

export default {
  id: 'export',
  handler: (router, { env, services, logger }) => {
    const exportPath = env.CONTENT_EXPORT_PATH || '/directus/astro-content';

    // Export all published content across all collections
    router.get('/all', async (req, res) => {
      if (!req.accountability?.admin) {
        return res.status(403).json({ error: 'Admin access required' });
      }

      const results = { exported: 0, errors: 0, details: [] };

      for (const collection of COLLECTIONS) {
        try {
          const { ItemsService } = services;
          const itemsService = new ItemsService(collection, {
            schema: req.schema,
            accountability: { admin: true },
          });

          const items = await itemsService.readByQuery({
            filter: { status: { _eq: 'published' } },
            limit: -1,
          });

          for (const item of items) {
            try {
              await exportItem(item, collection, exportPath, services, req.schema, logger);
              results.exported++;
              results.details.push({ collection, slug: item.slug, status: 'ok' });
            } catch (err) {
              results.errors++;
              results.details.push({ collection, slug: item.slug, status: 'error', message: err.message });
            }
          }
        } catch (err) {
          results.errors++;
          results.details.push({ collection, status: 'error', message: err.message });
        }
      }

      res.json(results);
    });

    // Export all published items in a specific collection
    router.get('/:collection', async (req, res) => {
      if (!req.accountability?.admin) {
        return res.status(403).json({ error: 'Admin access required' });
      }

      const { collection } = req.params;
      if (!COLLECTIONS.includes(collection)) {
        return res.status(404).json({ error: `Unknown collection: ${collection}` });
      }

      const results = { exported: 0, errors: 0, details: [] };

      try {
        const { ItemsService } = services;
        const itemsService = new ItemsService(collection, {
          schema: req.schema,
          accountability: { admin: true },
        });

        const items = await itemsService.readByQuery({
          filter: { status: { _eq: 'published' } },
          limit: -1,
        });

        for (const item of items) {
          try {
            await exportItem(item, collection, exportPath, services, req.schema, logger);
            results.exported++;
            results.details.push({ slug: item.slug, status: 'ok' });
          } catch (err) {
            results.errors++;
            results.details.push({ slug: item.slug, status: 'error', message: err.message });
          }
        }
      } catch (err) {
        results.errors++;
        results.details.push({ status: 'error', message: err.message });
      }

      res.json(results);
    });

    // Export a single item by slug (any status)
    router.get('/:collection/:slug', async (req, res) => {
      if (!req.accountability?.admin) {
        return res.status(403).json({ error: 'Admin access required' });
      }

      const { collection, slug } = req.params;
      if (!COLLECTIONS.includes(collection)) {
        return res.status(404).json({ error: `Unknown collection: ${collection}` });
      }

      try {
        const { ItemsService } = services;
        const itemsService = new ItemsService(collection, {
          schema: req.schema,
          accountability: { admin: true },
        });

        const items = await itemsService.readByQuery({
          filter: { slug: { _eq: slug } },
          limit: 1,
        });

        if (!items.length) {
          return res.status(404).json({ error: `No item found with slug "${slug}" in ${collection}` });
        }

        await exportItem(items[0], collection, exportPath, services, req.schema, logger);
        res.json({ exported: 1, slug, collection });
      } catch (err) {
        res.status(500).json({ error: err.message });
      }
    });
  },
};
