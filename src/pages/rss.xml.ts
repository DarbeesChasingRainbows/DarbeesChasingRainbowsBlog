import rss from '@astrojs/rss';
import type { APIContext } from 'astro';
import { SITE_DESCRIPTION, SITE_TITLE } from '../consts';
import { getBlogPosts, getProjects, getFieldNotes } from '../utils/getPosts';

export async function GET(context: APIContext) {
	const [blog, projects, fieldNotes] = await Promise.all([
		getBlogPosts(),
		getProjects(),
		getFieldNotes(),
	]);

	const items = [
		...blog.map((post) => ({
			title: post.data.title,
			pubDate: post.data.pubDate,
			description: post.data.description,
			categories: [post.data.category, ...post.data.tags],
			link: `/blog/${post.id}/`,
		})),
		...projects.map((post) => ({
			title: `[Project] ${post.data.title}`,
			pubDate: post.data.pubDate,
			description: post.data.description,
			categories: [post.data.category, ...post.data.tags],
			link: `/projects/${post.id}/`,
		})),
		...fieldNotes.map((post) => ({
			title: `[Field Notes] ${post.data.title}`,
			pubDate: post.data.pubDate,
			description: post.data.description,
			categories: [post.data.category, ...post.data.tags],
			link: `/field-notes/${post.id}/`,
		})),
	].sort((a, b) => new Date(b.pubDate).valueOf() - new Date(a.pubDate).valueOf());

	return rss({
		title: SITE_TITLE,
		description: SITE_DESCRIPTION,
		site: context.site!,
		items,
		customData: `<language>en-us</language>`,
	});
}
