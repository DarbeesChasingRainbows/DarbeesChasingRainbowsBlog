import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';
import { z } from 'astro/zod';
import { CONTENT_CATEGORIES } from './consts';

// Shared LLM/E-E-A-T fields — added to every collection so AI agents see
// consistent citable structure across blog posts, projects, and field notes.
const llmFields = {
	keyTakeaways: z.array(z.string()).optional(),
	sources: z
		.array(
			z.object({
				title: z.string(),
				url: z.url(),
				type: z.enum(['primary', 'secondary']).optional(),
			}),
		)
		.optional(),
	faq: z
		.array(
			z.object({
				question: z.string(),
				answer: z.string(),
			}),
		)
		.optional(),
	entityMentions: z.array(z.string()).optional(),
	aiSummary: z.string().optional(),
	// Accessible alt text for the hero/featured image. When omitted, the post
	// title is used (lossy — "RV Water Tank Alert System" is not a description
	// of the image). Recommended: describe what's in the image, not what the
	// post is about. Empty string ("") opts out of screen-reader announcement
	// for purely decorative images (rare).
	imageAlt: z.string().optional(),
	// Image attribution — credit the photographer/source
	imageAttribution: z
		.object({
			name: z.string(),
			url: z.url().optional(),
		})
		.optional(),
	// Preview/excerpt text shown in post listings
	preview: z.string().optional(),
};

const blog = defineCollection({
	loader: glob({
		base: './src/content/blog',
		pattern: ['**/*.{md,mdx}', '!**/_drafts/**', '!**/*.draft.{md,mdx}', '!**/*.wip.{md,mdx}'],
	}),
	schema: ({ image }) =>
		z.object({
			title: z.string(),
			description: z.string(),
			pubDate: z.coerce.date(),
			updatedDate: z.coerce.date().optional(),
			author: z.string().optional(),
			category: z.enum(CONTENT_CATEGORIES),
			tags: z.array(z.string()).default([]),
			featuredImage: image().optional().catch(undefined),
			heroImage: image().optional().catch(undefined),
			draft: z.boolean().default(false),
			...llmFields,
		}),
});

const projects = defineCollection({
	loader: glob({
		base: './src/content/projects',
		pattern: ['**/*.{md,mdx}', '!**/_drafts/**', '!**/*.draft.{md,mdx}', '!**/*.wip.{md,mdx}'],
	}),
	schema: ({ image }) =>
		z.object({
			title: z.string(),
			description: z.string(),
			pubDate: z.coerce.date(),
			updatedDate: z.coerce.date().optional(),
			category: z.enum(CONTENT_CATEGORIES),
			tags: z.array(z.string()).default([]),
			featuredImage: image().optional().catch(undefined),
			heroImage: image().optional().catch(undefined),
			difficulty: z.enum(['easy', 'medium', 'hard']).optional(),
			estimatedCost: z.string().optional(),
			estimatedTime: z.string().optional(),
			githubUrl: z.url().optional(),
			partsList: z
				.array(
					z.object({
						name: z.string(),
						quantity: z.number().optional(),
						url: z.url().optional(),
						notes: z.string().optional(),
					}),
				)
				.optional(),
			draft: z.boolean().default(false),
			...llmFields,
		}),
});

const fieldNotes = defineCollection({
	loader: glob({
		base: './src/content/field-notes',
		pattern: ['**/*.{md,mdx}', '!**/_drafts/**', '!**/*.draft.{md,mdx}', '!**/*.wip.{md,mdx}'],
	}),
	schema: ({ image }) =>
		z.object({
			title: z.string(),
			description: z.string(),
			pubDate: z.coerce.date(),
			location: z.string(),
			region: z.string().optional(),
			weather: z.string().optional(),
			category: z.enum(CONTENT_CATEGORIES),
			tags: z.array(z.string()).default([]),
			featuredImage: image().optional().catch(undefined),
			heroImage: image().optional().catch(undefined),
			includesHomeschool: z.boolean().default(false),
			saw: z.array(z.string()).optional(),
			heard: z.string().optional(),
			wondered: z.string().optional(),
			learned: z.string().optional(),
			draft: z.boolean().default(false),
			...llmFields,
		}),
});

const books = defineCollection({
	loader: glob({
		base: './src/content/books',
		pattern: ['**/*.{md,mdx}', '!**/_drafts/**', '!**/*.draft.{md,mdx}', '!**/*.wip.{md,mdx}'],
	}),
	schema: ({ image }) =>
		z.object({
			title: z.string(),
			description: z.string(),
			bookTitle: z.string(),
			author: z.string(),
			pubDate: z.coerce.date(),
			category: z.enum([
				'Kids',
				'Tweens',
				'Teens',
				'Family',
				'Homeschool',
				'Kingdom Farm',
				'Theology',
				'Nature',
				'History',
			]),
			ageRange: z.string().optional(),
			formatUsed: z.enum(['read-aloud', 'audiobook', 'independent', 'mixed']).optional(),
			rating: z.enum(['green', 'yellow', 'parent-read', 'red']),
			dadTake: z.string().optional(),
			momTake: z.string().optional(),
			kidsTake: z.string().optional(),
			readAloudValue: z.string().optional(),
			audiobookValue: z.string().optional(),
			educationalValue: z.string().optional(),
			contentNotes: z.string().optional(),
			worldviewNotes: z.string().optional(),
			tags: z.array(z.string()).default([]),
			featuredImage: image().optional().catch(undefined),
			heroImage: image().optional().catch(undefined),
			draft: z.boolean().default(false),
			// Books participate in the same E-E-A-T story as posts: AI-citable
			// summary, key takeaways, FAQ, sources, entity mentions, image alt.
			...llmFields,
		}),
});

export const collections = { blog, projects, 'field-notes': fieldNotes, books };
