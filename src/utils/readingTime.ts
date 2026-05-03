/**
 * Estimate reading time in minutes for a string of content.
 * Assumes ~225 words per minute.
 */
export function readingTime(content: string): number {
	const words = content.trim().split(/\s+/).length;
	const minutes = Math.ceil(words / 225);
	return Math.max(1, minutes);
}
