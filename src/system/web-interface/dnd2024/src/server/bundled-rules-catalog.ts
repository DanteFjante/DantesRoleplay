import { createHash } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { extname, relative, sep } from "node:path";

import type { RuleReadModel } from "../data/hub-types";

const SOURCE_ID = "dnd2024.source.srd-5.2.1";

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function boundedText(value: unknown, maximum: number): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value
    ? value
    : null;
}

function titleCase(value: string): string {
  return value.split("-")
    .filter(Boolean)
    .map((word) => `${word[0]?.toLocaleUpperCase() ?? ""}${word.slice(1)}`)
    .join(" ");
}

function filesBelow(root: string): string[] {
  const files: string[] = [];
  const visit = (directory: string) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = `${directory}${sep}${entry.name}`;
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile() && extname(entry.name).toLocaleLowerCase() === ".json") files.push(path);
    }
  };
  visit(root);
  return files.sort();
}

export function buildBundledRulesCatalog(entitiesRoot: string): RuleReadModel[] {
  const rules: RuleReadModel[] = [];
  for (const file of filesBelow(entitiesRoot)) {
    const bytes = readFileSync(file);
    const record = object(JSON.parse(bytes.toString("utf8")));
    const id = boundedText(record?.id, 400);
    const title = boundedText(record?.name, 200);
    const archetype = boundedText(record?.archetype, 300);
    const components = object(record?.components);
    const version = object(components?.["dnd2024.core.version"]);
    const source = object(components?.["dnd2024.core.source"]);
    const citations = Array.isArray(source?.citations) ? source.citations : [];
    const citation = citations.map(object).find((candidate) =>
      object(candidate?.sourceRef)?.entityId === SOURCE_ID
      && boundedText(candidate?.locator, 500) !== null);
    const locator = boundedText(citation?.locator, 500);
    const revision = version?.revision;
    if (
      !id || !id.startsWith("dnd2024.") || !title || !archetype?.startsWith("dnd2024.archetype.")
      || !Number.isInteger(revision) || Number(revision) < 1 || version?.status !== "active" || !locator
    ) continue;

    const relativePath = relative(entitiesRoot, file).split(sep).join("/");
    const segments = relativePath.split("/");
    if (segments.length < 2) continue;
    const category = titleCase(segments[0]);
    const subcategory = segments.slice(1, -1).map(titleCase).join(" · ");
    const presentation = object(components?.["dnd2024.core.presentation"]);
    const authoredSummary = boundedText(presentation?.summary, 2_000);
    rules.push({
      id,
      title,
      category,
      subcategory,
      path: ["entities", ...segments.slice(0, -1)].join("/"),
      contentFingerprint: createHash("sha256").update(bytes).digest("hex").toLocaleUpperCase(),
      summary: authoredSummary ?? `${subcategory || category} reference registered in the D&D 2024 catalog.`,
      revision: Number(revision),
      source: { id: SOURCE_ID, locator },
    });
  }

  return rules.sort((left, right) => left.category.localeCompare(right.category)
    || left.subcategory.localeCompare(right.subcategory)
    || left.title.localeCompare(right.title)
    || left.id.localeCompare(right.id));
}
