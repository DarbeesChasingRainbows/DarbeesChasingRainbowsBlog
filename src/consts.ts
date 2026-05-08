// Place any global data in this file.
// You can import this data from anywhere in your site by using the `import` keyword.

export const SITE_TITLE = 'Darbees Chasing Rainbows';
export const SITE_DESCRIPTION =
	'A Christian family building a more intentional, faith-rooted life through real constraints, honest experience, and the long road toward Kingdom Farm.';
export const SITE_URL = 'https://darbeeschasingrainbows.com';
export const SITE_AUTHOR = 'The Darbees';
export const SITE_TAGLINE = 'Faith-rooted family life under real constraints.';

export const SOCIAL_LINKS = {
	instagram: 'https://instagram.com/darbees.chasing.rainbows',
	github: 'https://github.com/darbeeschasingrainbows',
	email: 'mailto:hello@darbeeschasingrainbows.com',
};

export const NAV_LINKS = [
	{ href: '/start-here', label: 'Start Here' },
	{ href: '/rv-life', label: 'RV Life' },
	{ href: '/homeschool', label: 'Homeschool' },
	{ href: '/field-notes', label: 'Field Notes' },
	{ href: '/projects', label: 'Projects' },
	{ href: '/kingdom-farm', label: 'Kingdom Farm' },
	{ href: '/bookshelf', label: 'Bookshelf' },
];

// ---------------------------------------------------------------------------
// Content collection categories — used by blog, projects, and field-notes
// ---------------------------------------------------------------------------
export const CONTENT_CATEGORIES = [
	'RV Life',
	'Homeschool',
	'Kingdom Farm',
	'Faith & Reflections',
	'Field Notes',
	'Projects & Builds',
] as const;

export type ContentCategory = (typeof CONTENT_CATEGORIES)[number];

// Common tags used across content for tag pills and autocomplete
export const CONTENT_TAGS = [
	'RV',
	'Solar',
	'Water Systems',
	'DIY',
	'Automation',
	'Travel',
	'Homeschool',
	'Field Trip',
	'Nature',
	'Farm',
	'Garden',
	'Faith',
	'Family',
	'Book Review',
	'Tools',
	'Build',
	'Maintenance',
] as const;

// Cloudflare Web Analytics token. Set PUBLIC_CLOUDFLARE_ANALYTICS_TOKEN in
// .env (local) or in the Cloudflare Pages project env (prod). When empty, the
// beacon script in BaseHead.astro is not emitted.
export const CLOUDFLARE_ANALYTICS_TOKEN = import.meta.env.PUBLIC_CLOUDFLARE_ANALYTICS_TOKEN ?? '';

// Buttondown newsletter handle (the part after /embed-subscribe/ in the URL).
// Set PUBLIC_BUTTONDOWN_HANDLE in .env. When empty, NewsletterSignup falls
// back to the placeholder behavior defined in that component.
export const BUTTONDOWN_HANDLE = import.meta.env.PUBLIC_BUTTONDOWN_HANDLE ?? '';

// Difficulty → DaisyUI badge color. Used by both ProjectCard and the project
// post layout. Single source of truth so both stay in sync.
export const DIFFICULTY_COLORS = {
	easy: 'badge-success',
	medium: 'badge-warning',
	hard: 'badge-error',
} as const;
export type Difficulty = keyof typeof DIFFICULTY_COLORS;

// ---------------------------------------------------------------------------
// Bookshelf vocabulary — used by /bookshelf and (when added) /bookshelf/[id]
// ---------------------------------------------------------------------------

// Rating system the family uses for every book review. Maps 1:1 to the
// `rating` enum in the `books` content collection schema.
export const BOOKSHELF_RATINGS = [
	{
		icon: '🟢',
		label: 'Green Light',
		desc: 'We would hand this to our kids confidently.',
		color: 'border-green-300 bg-green-50',
	},
	{
		icon: '🟡',
		label: 'Yellow Light',
		desc: 'Worth reading with discussion.',
		color: 'border-yellow-300 bg-yellow-50',
	},
	{
		icon: '📖',
		label: 'Parent Read First',
		desc: 'Preview before handing over.',
		color: 'border-blue-300 bg-blue-50',
	},
	{
		icon: '🔴',
		label: 'Red Light',
		desc: 'Not for our family.',
		color: 'border-red-300 bg-red-50',
	},
] as const;

// Discernment lenses we apply when reviewing — shown as a checklist on the
// bookshelf landing page so readers know what we're looking for.
export const BOOKSHELF_LOOK_FOR = [
	'Truth',
	'Goodness',
	'Beauty',
	'Faith & worldview',
	'Family portrayal',
	'Authority & courage',
	'Repentance & growth',
	'Content notes',
	'Formation',
	'Read-aloud value',
	'Audiobook value',
	'Age fit',
] as const;

// Books that are queued for review but not written yet. Edit this list when
// you finish a review (move the title into the `books` collection and remove
// it from here).
export const BOOKSHELF_COMING_SOON = [
	'Little Britches series — Ralph Moody',
	'The Green Ember — S.D. Smith',
	'Favorite family read-alouds for the road',
	'Books for Christian tweens',
	"What we're reading for Kingdom Farm",
] as const;

// Maps the `rating` enum from the books collection schema to the display
// treatment used everywhere a book rating is shown (detail page, cards).
// Keep in lockstep with the enum values in content.config.ts.
export const BOOKSHELF_RATING_MAP = {
	green: BOOKSHELF_RATINGS[0],
	yellow: BOOKSHELF_RATINGS[1],
	'parent-read': BOOKSHELF_RATINGS[2],
	red: BOOKSHELF_RATINGS[3],
} as const;

// ---------------------------------------------------------------------------
// Brand persona — single source of truth used across the site, JSON-LD,
// llms.txt, knowledge.json, and the EntityCard component.
// Use these constants verbatim everywhere so AI agents see one consistent
// entity ("Darbees Chasing Rainbows" / "The Darbees" — Rome, GA).
// ---------------------------------------------------------------------------
export const BRAND_ENTITY = 'Darbees Chasing Rainbows';
export const BRAND_FAMILY = 'The Darbees';
export const BRAND_TAGLINE = SITE_TAGLINE;
export const BRAND_BIO =
	'The Darbees are a Christian family in Rome, Georgia documenting the work of building a more intentional, faith-rooted life through RV life, homeschool field notes, useful projects, book discernment, and the slow build toward Kingdom Farm. We publish primary-source stories — real photos, real timelines, real numbers.';

export const BRAND_LOCATION = {
	city: 'Rome',
	region: 'GA',
	regionName: 'Georgia',
	country: 'US',
	countryName: 'United States',
};

export const BRAND_CONTACT = 'hello@darbeeschasingrainbows.com';

export const BRAND_VALUES = [
	'Faith first — Christ-centered family living',
	'Primary-source storytelling — real photos, real timelines',
	'Practical over polished — useful projects beat pretty ones',
	'Honest reflection — failures get the same airtime as wins',
	'Long-obedience — building toward Kingdom Farm one season at a time',
];

export const BRAND_TOPICS = [
	'RV life with a young family',
	'Homeschool field notes from the road',
	'Faith and family rhythms',
	'Practical DIY and maker projects',
	'Kingdom Farm: building a working homestead',
	'Travel and field journaling across the U.S.',
];

export const BRAND_SAME_AS = [SOCIAL_LINKS.instagram, SOCIAL_LINKS.github];
