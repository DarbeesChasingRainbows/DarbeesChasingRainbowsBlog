// Place any global data in this file.
// You can import this data from anywhere in your site by using the `import` keyword.

export const SITE_TITLE = 'Darbees Chasing Rainbows';
export const SITE_DESCRIPTION =
	'Family, faith, RV life, homeschool field notes, useful projects, and the road toward Kingdom Farm.';
export const SITE_URL = 'https://darbeeschasingrainbows.com';
export const SITE_AUTHOR = 'The Darbees';
export const SITE_TAGLINE = 'Honest family life on the road to deeper roots.';

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

// Cloudflare Web Analytics token (set via env var or here)
export const CLOUDFLARE_ANALYTICS_TOKEN = '';

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
	'The Darbees are a Christian family in Rome, Georgia documenting RV life, homeschool field notes, faith, and the slow build toward Kingdom Farm. We publish primary-source stories — real photos, real timelines, real numbers.';

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
