import { defineConfig, devices } from '@playwright/test';

// Smoke-test config. Goals (intentionally narrow):
//   - Verify the production build renders the routes that matter
//   - Verify XML feeds (RSS, sitemap) are well-formed
//   - Catch regressions caused by content-collection changes, layout
//     refactors, or build-time errors that don't surface as type errors
//
// What we do NOT test here:
//   - Visual regression (would belong in a separate Lighthouse/percy job)
//   - Cross-browser matrix (chromium-only is plenty for a static site)
//   - Slow user flows (no SSR, no auth, no forms that submit somewhere)
//
// `reuseExistingServer: !process.env.CI` means locally we'll reuse whatever
// dev/preview server happens to be running on port 4321; in CI we always
// start our own preview server from a fresh build.

export default defineConfig({
	testDir: './tests',
	timeout: 30 * 1000,
	expect: { timeout: 5 * 1000 },
	fullyParallel: true,
	forbidOnly: !!process.env.CI,
	retries: process.env.CI ? 1 : 0,
	workers: process.env.CI ? 2 : undefined,
	reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',

	use: {
		baseURL: 'http://localhost:4321',
		trace: 'on-first-retry',
	},

	projects: [
		{
			name: 'chromium',
			use: { ...devices['Desktop Chrome'] },
		},
	],

	webServer: {
		command: 'npm run preview',
		url: 'http://localhost:4321/',
		timeout: 120 * 1000,
		reuseExistingServer: !process.env.CI,
	},
});
