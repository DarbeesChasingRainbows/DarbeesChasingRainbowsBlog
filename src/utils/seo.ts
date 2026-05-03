import {
	SITE_DESCRIPTION,
	SITE_TITLE,
	SITE_URL,
	BRAND_ENTITY,
	BRAND_FAMILY,
	BRAND_BIO,
	BRAND_LOCATION,
	BRAND_CONTACT,
	BRAND_SAME_AS,
	BRAND_TOPICS,
} from '../consts';

export interface SeoProps {
	title?: string;
	description?: string;
	image?: string;
	canonicalURL?: URL | string;
	ogType?: 'website' | 'article';
	publishDate?: Date;
	updatedDate?: Date;
	tags?: string[];
}

export function buildSeoTitle(title?: string): string {
	if (!title || title === SITE_TITLE) return SITE_TITLE;
	return `${title} · ${SITE_TITLE}`;
}

export function buildSeoDescription(description?: string): string {
	return description?.trim() || SITE_DESCRIPTION;
}

export function buildArticleJsonLd(opts: {
	title: string;
	description: string;
	url: string;
	image?: string;
	publishDate?: Date;
	updatedDate?: Date;
	author?: string;
}) {
	return {
		'@context': 'https://schema.org',
		'@type': 'Article',
		headline: opts.title,
		description: opts.description,
		url: opts.url,
		image: opts.image,
		datePublished: opts.publishDate?.toISOString(),
		dateModified: (opts.updatedDate ?? opts.publishDate)?.toISOString(),
		author: {
			'@type': 'Person',
			name: opts.author ?? 'The Darbees',
		},
		publisher: {
			'@type': 'Organization',
			name: SITE_TITLE,
			url: SITE_URL,
		},
	};
}

// ---------------------------------------------------------------------------
// Multi-block JSON-LD builders for E-E-A-T + LLM citation optimization.
// ---------------------------------------------------------------------------

const ORG_LOGO = `${SITE_URL}/images/brand/logo.svg`;
const ORG_ID = `${SITE_URL}#organization`;
const PERSON_ID = `${SITE_URL}#family`;
const WEBSITE_ID = `${SITE_URL}#website`;

export function buildOrganizationJsonLd() {
	return {
		'@context': 'https://schema.org',
		'@type': 'Organization',
		'@id': ORG_ID,
		name: BRAND_ENTITY,
		alternateName: BRAND_FAMILY,
		url: SITE_URL,
		logo: ORG_LOGO,
		description: BRAND_BIO,
		email: BRAND_CONTACT,
		sameAs: BRAND_SAME_AS,
		knowsAbout: BRAND_TOPICS,
		location: {
			'@type': 'Place',
			address: {
				'@type': 'PostalAddress',
				addressLocality: BRAND_LOCATION.city,
				addressRegion: BRAND_LOCATION.region,
				addressCountry: BRAND_LOCATION.country,
			},
		},
	};
}

export function buildPersonJsonLd() {
	return {
		'@context': 'https://schema.org',
		'@type': 'Person',
		'@id': PERSON_ID,
		name: BRAND_FAMILY,
		alternateName: BRAND_ENTITY,
		description: BRAND_BIO,
		url: SITE_URL,
		image: ORG_LOGO,
		sameAs: BRAND_SAME_AS,
		knowsAbout: BRAND_TOPICS,
		homeLocation: {
			'@type': 'Place',
			address: {
				'@type': 'PostalAddress',
				addressLocality: BRAND_LOCATION.city,
				addressRegion: BRAND_LOCATION.region,
				addressCountry: BRAND_LOCATION.country,
			},
		},
	};
}

export function buildWebSiteJsonLd() {
	return {
		'@context': 'https://schema.org',
		'@type': 'WebSite',
		'@id': WEBSITE_ID,
		name: SITE_TITLE,
		alternateName: BRAND_FAMILY,
		url: SITE_URL,
		description: SITE_DESCRIPTION,
		inLanguage: 'en-US',
		publisher: { '@id': ORG_ID },
		potentialAction: {
			'@type': 'SearchAction',
			target: {
				'@type': 'EntryPoint',
				urlTemplate: `${SITE_URL}/blog?q={search_term_string}`,
			},
			'query-input': 'required name=search_term_string',
		},
	};
}

export interface BlogPostingOpts {
	title: string;
	description: string;
	url: string;
	image?: string;
	publishDate?: Date;
	updatedDate?: Date;
	author?: string;
	category?: string;
	tags?: string[];
	aiSummary?: string;
	wordCount?: number;
}

export function buildBlogPostingJsonLd(opts: BlogPostingOpts) {
	return {
		'@context': 'https://schema.org',
		'@type': 'BlogPosting',
		mainEntityOfPage: { '@type': 'WebPage', '@id': opts.url },
		headline: opts.title,
		description: opts.description,
		abstract: opts.aiSummary ?? opts.description,
		url: opts.url,
		image: opts.image,
		datePublished: opts.publishDate?.toISOString(),
		dateModified: (opts.updatedDate ?? opts.publishDate)?.toISOString(),
		articleSection: opts.category,
		keywords: opts.tags?.join(', '),
		wordCount: opts.wordCount,
		inLanguage: 'en-US',
		isAccessibleForFree: true,
		author: { '@id': PERSON_ID, '@type': 'Person', name: opts.author ?? BRAND_FAMILY },
		publisher: { '@id': ORG_ID },
	};
}

export interface FaqItem {
	question: string;
	answer: string;
}

export function buildFaqJsonLd(faq: FaqItem[]) {
	return {
		'@context': 'https://schema.org',
		'@type': 'FAQPage',
		mainEntity: faq.map((item) => ({
			'@type': 'Question',
			name: item.question,
			acceptedAnswer: {
				'@type': 'Answer',
				text: item.answer,
			},
		})),
	};
}

export interface HowToOpts {
	name: string;
	description: string;
	image?: string;
	estimatedCost?: string;
	totalTime?: string;
	supplies?: Array<{ name: string; quantity?: number }>;
	steps?: Array<{ name: string; text: string }>;
}

export function buildHowToJsonLd(opts: HowToOpts) {
	const node: Record<string, unknown> = {
		'@context': 'https://schema.org',
		'@type': 'HowTo',
		name: opts.name,
		description: opts.description,
		image: opts.image,
	};
	if (opts.estimatedCost) {
		node.estimatedCost = {
			'@type': 'MonetaryAmount',
			currency: 'USD',
			value: opts.estimatedCost,
		};
	}
	if (opts.totalTime) node.totalTime = opts.totalTime;
	if (opts.supplies?.length) {
		node.supply = opts.supplies.map((s) => ({
			'@type': 'HowToSupply',
			name: s.name,
		}));
	}
	if (opts.steps?.length) {
		node.step = opts.steps.map((s, i) => ({
			'@type': 'HowToStep',
			position: i + 1,
			name: s.name,
			text: s.text,
		}));
	}
	return node;
}

export function buildBreadcrumbJsonLd(items: Array<{ label: string; href?: string }>) {
	return {
		'@context': 'https://schema.org',
		'@type': 'BreadcrumbList',
		itemListElement: items.map((item, i) => ({
			'@type': 'ListItem',
			position: i + 1,
			name: item.label,
			...(item.href ? { item: new URL(item.href, SITE_URL).toString() } : {}),
		})),
	};
}
