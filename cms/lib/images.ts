import { dirname, fromFileUrl, join } from "jsr:@std/path@^1.1.2";
import type { CollectionKey } from "./schemas.ts";

const CONTENT_ROOT = join(
  dirname(fromFileUrl(import.meta.url)),
  "../../src/content",
);

/**
 * Save an uploaded image file to the collection's images folder.
 * Returns the relative path from the collection folder (e.g., ./images/photo.jpg).
 */
export async function saveImageFile(
  collection: CollectionKey,
  slug: string,
  file: File,
): Promise<string> {
  // Validate file type
  if (!file.type.startsWith("image/")) {
    throw new Error("Only image files are allowed");
  }

  // Create images subfolder for the collection
  const collectionFolder = join(CONTENT_ROOT, collection);
  const imagesFolder = join(collectionFolder, "images");
  try {
    Deno.mkdirSync(imagesFolder, { recursive: true });
  } catch {
    // Folder already exists
  }

  // Generate safe filename
  const ext = file.name.split(".").pop() ?? "jpg";
  const baseName = slug.replace(/[^a-z0-9-]/g, "-");
  const timestamp = Date.now();
  const filename = `${baseName}-${timestamp}.${ext}`;
  const imagePath = join(imagesFolder, filename);

  // Write file
  const bytes = await file.arrayBuffer();
  await Deno.writeFile(imagePath, new Uint8Array(bytes));

  // Return relative path from collection folder for frontmatter
  return `./images/${filename}`;
}

/**
 * List all images in a collection's images folder.
 */
export function listCollectionImages(
  collection: CollectionKey,
): string[] {
  const imagesFolder = join(CONTENT_ROOT, collection, "images");
  try {
    const entries = Array.from(Deno.readDirSync(imagesFolder));
    return entries
      .filter((entry) => entry.isFile && isImageFile(entry.name))
      .map((entry) => `./images/${entry.name}`);
  } catch {
    // Folder doesn't exist yet
    return [];
  }
}

function isImageFile(filename: string): boolean {
  const ext = filename.split(".").pop()?.toLowerCase();
  return [
    "jpg",
    "jpeg",
    "png",
    "gif",
    "webp",
    "avif",
    "svg",
  ].includes(ext ?? "");
}
