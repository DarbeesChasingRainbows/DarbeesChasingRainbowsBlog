export type FrontmatterValue = string | boolean | number | string[] | Record<
  string,
  unknown
>[] | { name?: string; url?: string };
export type Frontmatter = Record<string, FrontmatterValue | undefined>;

export interface MdxDocument {
  frontmatter: Frontmatter;
  body: string;
}

export function splitMdx(source: string): MdxDocument {
  if (!source.startsWith("---\n")) {
    return { frontmatter: {}, body: source };
  }

  const end = source.indexOf("\n---", 4);
  if (end === -1) {
    return { frontmatter: {}, body: source };
  }

  const yaml = source.slice(4, end).trim();
  const body = source.slice(end + 5).replace(/^\r?\n/, "");
  return { frontmatter: parseSimpleYaml(yaml), body };
}

export function parseSimpleYaml(yaml: string): Frontmatter {
  const result: Frontmatter = {};
  const lines = yaml.split(/\r?\n/);

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (!line.trim() || line.trim().startsWith("#") || line.startsWith(" ")) {
      continue;
    }

    const match = line.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
    if (!match) {
      continue;
    }

    const [, key, rawValue] = match;
    const following: string[] = [];
    let cursor = index + 1;
    while (cursor < lines.length && /^\s+/.test(lines[cursor])) {
      following.push(lines[cursor]);
      cursor++;
    }

    if (following.length > 0 && rawValue.trim() === "") {
      result[key] = parseBlockValue(following);
      index = cursor - 1;
      continue;
    }

    if (rawValue.trim() === ">-") {
      result[key] = following.map((item) => item.trim()).join(" ").trim();
      index = cursor - 1;
      continue;
    }

    result[key] = parseScalar(rawValue.trim());
  }

  return result;
}

function parseBlockValue(lines: string[]): FrontmatterValue {
  const trimmed = lines.map((line) => line.trim()).filter(Boolean);
  if (trimmed.every((line) => line.startsWith("- "))) {
    return trimmed.map((line) => stripQuotes(line.slice(2).trim()));
  }
  return trimmed.join("\n");
}

function parseScalar(value: string): FrontmatterValue {
  if (value === "true") return true;
  if (value === "false") return false;
  if (value.startsWith("[") && value.endsWith("]")) {
    return value.slice(1, -1).split(",").map((item) => stripQuotes(item.trim()))
      .filter(Boolean);
  }
  return stripQuotes(value.replace(/\s+#.*$/, ""));
}

function stripQuotes(value: string): string {
  if (
    (value.startsWith("'") && value.endsWith("'")) ||
    (value.startsWith('"') && value.endsWith('"'))
  ) {
    return value.slice(1, -1);
  }
  return value;
}

export function serializeMdx(frontmatter: Frontmatter, body: string): string {
  return `---\n${serializeFrontmatter(frontmatter)}---\n\n${body.trim()}\n`;
}

export function serializeFrontmatter(frontmatter: Frontmatter): string {
  const lines: string[] = [];

  for (const [key, value] of Object.entries(frontmatter)) {
    if (
      value === undefined || value === "" ||
      (Array.isArray(value) && value.length === 0)
    ) {
      continue;
    }

    // Split imageAttribution object into separate fields for CMS
    if (
      key === "imageAttribution" && typeof value === "object" && "name" in value
    ) {
      const attr = value as { name?: string; url?: string };
      if (attr.name) {
        lines.push(`imageAttributionName: ${quoteYaml(attr.name)}`);
      }
      if (attr.url) {
        lines.push(`imageAttributionUrl: ${quoteYaml(attr.url)}`);
      }
      continue;
    }

    if (typeof value === "boolean" || typeof value === "number") {
      lines.push(`${key}: ${value}`);
      continue;
    }

    if (Array.isArray(value)) {
      lines.push(`${key}:`);
      for (const item of value) {
        if (typeof item === "string") {
          lines.push(`  - ${quoteYaml(item)}`);
        } else {
          lines.push(`  - ${JSON.stringify(item)}`);
        }
      }
      continue;
    }

    if (typeof value === "string" && value.includes("\n")) {
      lines.push(`${key}: >-`);
      for (const line of value.split(/\r?\n/)) {
        lines.push(`  ${line}`);
      }
      continue;
    }

    lines.push(`${key}: ${quoteYaml(String(value))}`);
  }

  return `${lines.join("\n")}\n`;
}

function quoteYaml(value: string): string {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return value;
  }

  const escaped = value.replaceAll("'", "''");
  return `'${escaped}'`;
}
