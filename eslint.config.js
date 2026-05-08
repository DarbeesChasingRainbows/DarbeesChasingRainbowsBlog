// ESLint v9 flat config.
// Lints .astro, .ts, .tsx, .js, .mjs.
//
// Philosophy: catch real bugs and accessibility issues, don't fight the
// formatter. Prettier owns whitespace; ESLint owns correctness.
//
// Key choices:
// - eslint-plugin-astro recommended preset → catches a11y + Astro-specific issues
// - typescript-eslint recommended → catches TS misuse without going strict-everywhere
// - We turn off rules that conflict with our existing patterns (unused vars
//   in destructuring, const-only arrow functions). Tighten over time.

import astro from 'eslint-plugin-astro';
import tseslint from 'typescript-eslint';

export default [
	// Ignore non-source files (must be the first item in flat config).
	{
		ignores: [
			'dist/',
			'.astro/',
			'node_modules/',
			'public/',
			'package-lock.json',
			'playwright-report/',
			'test-results/',
		],
	},

	// TypeScript / JavaScript files
	...tseslint.configs.recommended,
	{
		files: ['**/*.{ts,tsx,js,mjs}'],
		rules: {
			// Allow underscore-prefixed unused vars (common destructuring pattern)
			'@typescript-eslint/no-unused-vars': [
				'warn',
				{ argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
			],
			// Astro components emit imports that look unused to TS but aren't.
			'@typescript-eslint/no-unused-expressions': 'off',
		},
	},

	// Astro components — `recommended` includes Astro-specific checks
	// (no-unused-define-vars-in-style, valid-compile, semi, etc.) but NOT
	// the jsx-a11y preset (that requires installing eslint-plugin-jsx-a11y
	// separately and is on the Phase 4 list).
	...astro.configs.recommended,
];
