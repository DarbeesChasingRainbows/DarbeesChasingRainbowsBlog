import { type CollectionKey, collections } from "./schemas.ts";
import type { Frontmatter } from "./frontmatter.ts";
import { saveImageFile } from "./images.ts";

export async function frontmatterFromRequest(
  collection: CollectionKey,
  req: Request,
): Promise<{ slug: string; frontmatter: Frontmatter; body: string }> {
  const form = await req.formData();
  const slug = String(form.get("slug") ?? "");
  const body = String(form.get("body") ?? "");
  const frontmatter: Frontmatter = {};

  for (const field of collections[collection].fields) {
    const raw = form.get(field.name);

    if (field.kind === "checkbox") {
      frontmatter[field.name] = raw === "true";
      continue;
    }

    if (raw === null) {
      continue;
    }

    const value = String(raw).trim();
    if (!value) {
      continue;
    }

    if (field.kind === "tags") {
      frontmatter[field.name] = splitList(value.replaceAll(",", "\n"));
      continue;
    }

    if (field.kind === "list") {
      frontmatter[field.name] = splitList(value);
      continue;
    }

    // Combine imageAttributionName and imageAttributionUrl into imageAttribution object
    if (
      field.name === "imageAttributionName" ||
      field.name === "imageAttributionUrl"
    ) {
      // Skip individual fields - they'll be combined below
      continue;
    }

    if (
      field.name === "faq" || field.name === "sources" ||
      field.name === "partsList"
    ) {
      frontmatter[field.name] = value;
      continue;
    }

    if (field.kind === "image") {
      const file = form.get(`${field.name}_file`);
      if (file instanceof File && file.size > 0) {
        const imagePath = await saveImageFile(collection, slug, file);
        frontmatter[field.name] = imagePath;
      } else {
        frontmatter[field.name] = value;
      }
      continue;
    }

    frontmatter[field.name] = value;
  }

  // Combine imageAttributionName and imageAttributionUrl into imageAttribution object
  const attributionName = form.get("imageAttributionName");
  const attributionUrl = form.get("imageAttributionUrl");
  if (attributionName || attributionUrl) {
    // Validate URL if provided
    if (attributionUrl && String(attributionUrl).trim()) {
      const url = String(attributionUrl).trim();
      try {
        new URL(url);
      } catch {
        throw new Error("Invalid image attribution URL");
      }
    }
    frontmatter.imageAttribution = {
      name: attributionName ? String(attributionName) : undefined,
      url: attributionUrl ? String(attributionUrl) : undefined,
    };
  }

  // Validate preview length
  const preview = form.get("preview");
  if (preview && String(preview).length > 200) {
    throw new Error("Preview/excerpt must be 200 characters or less");
  }

  return { slug, frontmatter, body };
}

function splitList(value: string): string[] {
  return value.split(/\r?\n/).map((item) => item.trim()).filter(Boolean);
}
