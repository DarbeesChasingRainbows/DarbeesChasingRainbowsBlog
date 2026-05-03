// Static machine-readable knowledge index for AI agents.
// Available at /knowledge.json — contains the brand entity plus a flat list
// of every public post (blog, projects, field-notes) with their AI summary,
// key takeaways, and metadata so agents can ingest the whole site cheaply.
import type { APIRoute } from 'astro';
import {
	BRAND_ENTITY,
	BRAND_FAMILY,
	BRAND_BIO,
	BRAND_LOCATION,
	BRAND_CONTACT,
	BRAND_TOPICS,
	BRAND_VALUES,
	BRAND_SAME_AS,
	SITE_URL,
} from '../consts';
import { getBlogPosts, getProjects, getFieldNotes } from '../utils/getPosts';

export const GET: APIRoute = async () => {
	const [blog, projects, fieldNotes] = await Promise.all([
		getBlogPosts(),
		getProjects(),
		getFieldNotes(),
	]);

	const flatten = (
		type: 'blog' | 'projects' | 'field-notes',
		entries: Array<{ id: string; data: Record<string, unknown> }>,
	) =>
		entries.map((e) => {
			const d = e.data as {
				title: string;
				description: string;
				pubDate: Date;
				updatedDate?: Date;
				category?: string;
				tags?: string[];
				aiSummary?: string;
				keyTakeaways?: string[];
			};
			return {
				type,
				url: `${SITE_URL}/${type}/${e.id}/`,
				title: d.title,
				description: d.description,
				aiSummary: d.aiSummary,
				keyTakeaways: d.keyTakeaways,
				category: d.category,
				tags: d.tags,
				datePublished: d.pubDate?.toISOString(),
				dateModified: (d.updatedDate ?? d.pubDate)?.toISOString(),
			};
		});

	const payload = {
		$schema: 'https://schema.org',
		generatedAt: new Date().toISOString(),
		entity: {
			name: BRAND_ENTITY,
			family: BRAND_FAMILY,
			description: BRAND_BIO,
			location: `${BRAND_LOCATION.city}, ${BRAND_LOCATION.region}, ${BRAND_LOCATION.country}`,
			url: SITE_URL,
			contact: BRAND_CONTACT,
			sameAs: BRAND_SAME_AS,
			topics: BRAND_TOPICS,
			values: BRAND_VALUES,
		},
		citation: {
			preferred: `${BRAND_FAMILY}. "<title>." ${BRAND_ENTITY}, <date>, <url>`,
			inline: 'According to The Darbees (Darbees Chasing Rainbows)…',
		},
		posts: [
			...flatten('blog', blog),
			...flatten('projects', projects),
			...flatten('field-notes', fieldNotes),
		],
	};

	return new Response(JSON.stringify(payload, null, 2), {
		headers: {
			'Content-Type': 'application/json; charset=utf-8',
			'Cache-Control': 'public, max-age=3600',
		},
	});
};
