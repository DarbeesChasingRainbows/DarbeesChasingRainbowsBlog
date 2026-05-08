// @ts-check

import mdx from '@astrojs/mdx';
import sitemap from '@astrojs/sitemap';
import icon from 'astro-icon';
import tailwindcss from '@tailwindcss/vite';
import cloudflare from '@astrojs/cloudflare';
import { defineConfig, fontProviders } from 'astro/config';

// https://astro.build/config
export default defineConfig({
	site: 'https://darbeeschasingrainbows.com',
	adapter: cloudflare(),
	integrations: [mdx(), sitemap(), icon()],
	vite: {
		plugins: [tailwindcss()],
	},
	fonts: [
		{
			provider: fontProviders.google(),
			name: 'Nunito',
			cssVariable: '--font-nunito',
			fallbacks: ['system-ui', 'sans-serif'],
			weights: [400, 600, 700, 800, 900],
		},
		{
			provider: fontProviders.google(),
			name: 'Inter',
			cssVariable: '--font-inter',
			fallbacks: ['system-ui', 'sans-serif'],
			weights: [400, 500, 600, 700],
		},
		{
			provider: fontProviders.google(),
			name: 'Fraunces',
			cssVariable: '--font-editorial',
			fallbacks: ['Georgia', 'serif'],
			weights: [400, 700, 900],
			styles: ['normal', 'italic'],
		},
	],
});
