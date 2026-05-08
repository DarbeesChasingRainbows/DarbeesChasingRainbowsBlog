import { test, expect } from '@playwright/test';

// ---------------------------------------------------------------------------
// Smoke tests — these don't try to be exhaustive. Each one answers ONE of:
//   "Did the build catastrophically break this surface?"
// If a test starts failing, it means a refactor touched something more
// important than it was supposed to. Treat failures as signal, not noise.
// ---------------------------------------------------------------------------

test.describe('Routes render', () => {
	test('homepage shows the masthead headline', async ({ page }) => {
		await page.goto('/');
		// The hero h1 is the canonical "did the home page render at all" probe.
		// Its text spans multiple lines via inline elements; assert on a
		// substring rather than exact content so a tweak to the wordmark
		// doesn't break the smoke test.
		await expect(page.locator('h1').first()).toContainText('Chasing');
		await expect(page.locator('h1').first()).toContainText('Kingdom');
	});

	test('blog index lists at least one post', async ({ page }) => {
		await page.goto('/blog/');
		// The "featured" section + the archive grid both render PostCard.
		// At least one card with a title-sweep span should be visible.
		const cards = page.locator('.title-sweep');
		await expect(cards.first()).toBeVisible();
	});

	test('a real blog post renders end-to-end', async ({ page }) => {
		await page.goto('/blog/homeschooling-under-constraints/');
		await expect(page.locator('h1').first()).toContainText("Homeschooling Under Constraints");
		// Breadcrumbs prove PostLayout's Breadcrumbs component is wired up.
		await expect(page.locator('nav[aria-label="Breadcrumb"]')).toContainText('Blog');
		// "Back to all posts" proves the layout's collection-aware footer works.
		await expect(page.getByRole('link', { name: /back to all posts/i })).toBeVisible();
	});

	test('a real project post renders + shows parts list and metadata grid', async ({ page }) => {
		await page.goto('/projects/off-grid-power-solar-setup/');
		await expect(page.locator('h1').first()).toContainText('Off-Grid Power');
		// Metadata grid (slot="meta" content) — Difficulty cell.
		await expect(page.getByText('Difficulty', { exact: true })).toBeVisible();
		// Parts list (slot="before-content") — table has the part name.
		await expect(page.getByText('200W monocrystalline panel')).toBeVisible();
	});

	test('a real field note renders + shows the location grid', async ({ page }) => {
		await page.goto('/field-notes/bulow-plantation-ruins/');
		await expect(page.locator('h1').first()).toContainText('Bulow');
		// "Location" appears twice on this page: once in the layout's slot="meta"
		// metadata grid (under <header>), and once inside the FieldNotesBlock
		// component embedded in the MDX content. We just need either to be
		// visible — pick the first match.
		await expect(page.getByText('Location', { exact: true }).first()).toBeVisible();
	});

	test('bookshelf renders even with an empty books collection', async ({ page }) => {
		await page.goto('/bookshelf/');
		await expect(page.getByText('Green Light').first()).toBeVisible();
		await expect(page.getByText('Carry On, Mr. Bowditch').first()).toBeVisible();
	});

	test('homeschool category page shows live posts from the blog', async ({ page }) => {
		await page.goto('/homeschool/');
		// The "From the blog" section appears when we have real posts.
		await expect(page.getByText('From the blog')).toBeVisible();
	});

	test('kingdom-farm category page shows live posts from the blog', async ({ page }) => {
		await page.goto('/kingdom-farm/');
		await expect(page.getByText('From the blog')).toBeVisible();
	});
});

test.describe('Feeds and discovery surfaces', () => {
	test('RSS feed is well-formed XML and lists at least one item', async ({ request }) => {
		const r = await request.get('/rss.xml');
		expect(r.status()).toBe(200);
		expect(r.headers()['content-type']).toMatch(/xml/);
		const body = await r.text();
		expect(body).toContain('<rss');
		expect(body).toMatch(/<item>/);
	});

	test('sitemap index is well-formed', async ({ request }) => {
		const r = await request.get('/sitemap-index.xml');
		expect(r.status()).toBe(200);
		const body = await r.text();
		expect(body).toContain('<sitemapindex');
	});

	test('llms.txt is served as a static asset', async ({ request }) => {
		const r = await request.get('/llms.txt');
		expect(r.status()).toBe(200);
	});

	test('knowledge.json endpoint returns valid JSON', async ({ request }) => {
		const r = await request.get('/knowledge.json');
		expect(r.status()).toBe(200);
		const body = await r.json();
		expect(typeof body).toBe('object');
	});
});

test.describe('Archive routes', () => {
	test('a real category archive lists matching posts', async ({ page }) => {
		// "RV Life" is a category we know exists on at least one post.
		await page.goto('/category/RV%20Life/');
		await expect(page.locator('h1').first()).toContainText('RV Life');
		await expect(page.locator('.title-sweep').first()).toBeVisible();
	});

	test('a real tag archive lists matching posts', async ({ page }) => {
		// "rv" is a tag we know exists on at least one post.
		await page.goto('/tag/rv/');
		await expect(page.locator('h1').first()).toContainText('rv');
		await expect(page.locator('.title-sweep').first()).toBeVisible();
	});
});

test.describe('JSON-LD invariants', () => {
	test('post page emits exactly one BreadcrumbList JSON-LD block', async ({ page }) => {
		await page.goto('/projects/off-grid-power-solar-setup/');
		const blocks = await page.$$eval('script[type="application/ld+json"]', (scripts) =>
			scripts
				.map((s) => {
					try {
						return JSON.parse(s.textContent || '');
					} catch {
						return null;
					}
				})
				.filter(Boolean),
		);
		const breadcrumbCount = blocks.filter(
			(b: { '@type'?: string }) => b['@type'] === 'BreadcrumbList',
		).length;
		// This regression has happened once already (PostLayout used to also
		// emit a BreadcrumbList in addition to the one Breadcrumbs.astro emits).
		// Keep this test forever to prevent it from coming back.
		expect(breadcrumbCount).toBe(1);
	});
});
