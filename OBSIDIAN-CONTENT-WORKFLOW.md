# Obsidian Content Workflow

Obsidian is the local editor for Darbees Chasing Rainbows content. Astro remains the source of truth and Cloudflare Pages only deploys committed site files.

## Why this replaces a CMS

- Obsidian provides the editing UI.
- Markdown files remain in `src/content/`.
- Astro content collections validate frontmatter at build time.
- Cloudflare Pages receives only committed files.
- `.obsidian/` is gitignored and never deployed.

## Setup

1. Open Obsidian.
2. Choose **Open folder as vault**.
3. Select `C:\Work\DarbeesChasingRainbows`.
4. Install and enable the **Templater** community plugin.
5. Install and enable **Custom File Extensions and Types**.
6. In Custom File Extensions and Types, map `mdx` to `markdown`.
7. In Templater settings, verify:
   - Template folder location: `.obsidian/templates`
   - Trigger Templater on new file creation: enabled
   - Folder templates:
     - `src/content/blog` -> `.obsidian/templates/new-blog-post.md`
     - `src/content/books` -> `.obsidian/templates/new-book-review.md`
     - `src/content/projects` -> `.obsidian/templates/new-project.md`
     - `src/content/field-notes` -> `.obsidian/templates/new-field-note.md`

## Creating a blog post

1. In Obsidian, create a new note in `src/content/blog`.
2. Templater prompts for a kebab-case slug.
3. Enter a slug such as `why-we-read-aloud-at-night`.
4. Templater creates `src/content/blog/why-we-read-aloud-at-night.mdx`.
5. Fill out the Properties/frontmatter fields.
6. Write the post body in MDX-compatible Markdown.
7. Keep `draft: true` until ready to publish.

## Creating a book review

1. Create a new note in `src/content/books`.
2. Enter a slug such as `the-hobbit`.
3. Fill in book fields including `bookTitle`, `author`, `rating`, `dadTake`, `momTake`, and `kidsTake`.
4. Write the review body in Markdown.
5. Keep `draft: true` until ready to publish.

## Publishing

1. Set `draft: false`.
2. Run validation:

```bash
npm run check
npm run build
```

1. Commit and push:

```bash
git add .
git commit -m "Add post: your-title"
git push
```

Cloudflare Pages deploys the committed Astro site.

## MDX in Obsidian

Use `.mdx` for Obsidian-authored content because the templates can include Astro components such as `Callout`.

Obsidian does not natively treat `.mdx` as Markdown. The **Custom File Extensions and Types** plugin must map `mdx` to `markdown`; otherwise Windows may open `.mdx` files in another app such as SSMS.

The repo's `src/content/_templates/*.mdx` files remain clean starter MDX files with no Templater syntax. Obsidian automation lives in `.obsidian/templates/`, reads those starter files, replaces the sample date with today's date, and writes the final `.mdx` post into the target collection.

## Safety rules

- Do not commit `.obsidian/`.
- Use `.wip.mdx` or `.draft.mdx` suffixes for incomplete scratch files if needed.
- Keep production-ready content in the proper collection folders.
- Let Astro validation catch schema errors before publishing.
