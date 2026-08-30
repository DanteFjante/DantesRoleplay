import type { RuleReadModel } from "../data/hub-types";
import { normalizeGameServerOrigin } from "./game-server-context.js";

const APPLICATION_ID = "dnd2024";
const COLLECTION_ID = "dnd2024";
const ENTITY_BRANCH = "entities";
const SOURCE_ID = "dnd2024.source.srd-5.2.1";
const PAGE_SIZE = 100;
const MAXIMUM_NODES = 4_096;
const MAXIMUM_RECORDS = 20_000;
const MAXIMUM_PAGES = 10_000;

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function boundedText(value: unknown, maximum: number): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value
    && !Array.from(value).some((character) => /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/u.test(character))
    ? value
    : null;
}

function recordContent(value: unknown): Record<string, unknown> | null {
  if (typeof value !== "string" || value.length === 0 || value.length > 500_000) return null;
  try {
    return object(JSON.parse(value));
  } catch {
    return null;
  }
}

function titleCasePathSegment(value: string): string {
  return value.split("-")
    .filter(Boolean)
    .map((word) => `${word[0]?.toLocaleUpperCase() ?? ""}${word.slice(1)}`)
    .join(" ");
}

function pathLabels(path: string): { category: string; subcategory: string } | null {
  const segments = path.split("/");
  if (segments[0] !== ENTITY_BRANCH || segments.length < 2 || segments.some((segment) => !segment)) return null;
  const category = titleCasePathSegment(segments[1]);
  const subcategory = segments.slice(2).map(titleCasePathSegment).join(" · ");
  return category ? { category, subcategory } : null;
}

function neutralSummary(category: string, subcategory: string): string {
  return `${subcategory || category} reference registered in the D&D 2024 catalog.`;
}

export function projectCatalogRuleSummary(value: unknown): RuleReadModel | null {
  const summary = object(value);
  const id = boundedText(summary?.qualifiedId, 400);
  const title = boundedText(summary?.name, 200);
  const path = boundedText(summary?.path, 500);
  const contentFingerprint = boundedText(summary?.contentFingerprint, 128);
  if (
    !id || !id.startsWith(`${APPLICATION_ID}.`) || !title || !path || !contentFingerprint
    || summary?.kind !== "entity" || summary.status !== "active"
  ) return null;
  const labels = pathLabels(path);
  if (!labels) return null;

  return {
    id,
    title,
    category: labels.category,
    subcategory: labels.subcategory,
    path,
    contentFingerprint,
    summary: neutralSummary(labels.category, labels.subcategory),
    revision: null,
    source: null,
  };
}

export function projectCatalogRuleRecord(value: unknown, expected: RuleReadModel): RuleReadModel | null {
  const envelope = object(value);
  const routeSummary = object(envelope?.summary);
  if (
    routeSummary?.qualifiedId !== expected.id || routeSummary.status !== "active"
    || routeSummary.kind !== "entity" || routeSummary.path !== expected.path
    || routeSummary.contentFingerprint !== expected.contentFingerprint
  ) return null;

  const record = recordContent(envelope?.contentJson);
  const title = boundedText(record?.name, 200);
  const archetype = boundedText(record?.archetype, 300);
  if (!record || record.id !== expected.id || !title || !archetype?.startsWith(`${APPLICATION_ID}.archetype.`)) return null;

  const components = object(record.components);
  const version = object(components?.["dnd2024.core.version"]);
  const revision = version?.revision;
  if (!Number.isInteger(revision) || Number(revision) < 1 || Number(revision) > 1_000_000 || version?.status !== "active") {
    return null;
  }

  const source = object(components?.["dnd2024.core.source"]);
  const citations = Array.isArray(source?.citations) ? source.citations : [];
  const sourceCitation = citations
    .map(object)
    .find((citation) => object(citation?.sourceRef)?.entityId === SOURCE_ID
      && boundedText(citation?.locator, 500) !== null);
  const locator = boundedText(sourceCitation?.locator, 500);
  if (!locator) return null;

  const presentation = object(components?.["dnd2024.core.presentation"]);
  const authoredSummary = boundedText(presentation?.summary, 2_000);
  return {
    ...expected,
    title,
    summary: authoredSummary ?? neutralSummary(expected.category, expected.subcategory),
    revision: Number(revision),
    source: { id: SOURCE_ID, locator },
  };
}

function isNodeEntry(value: Record<string, unknown>): boolean {
  return value.kind === 0 || value.kind === "node" || value.kind === "Node";
}

function isRecordEntry(value: Record<string, unknown>): boolean {
  return value.kind === 1 || value.kind === "record" || value.kind === "Record";
}

function compareRules(left: RuleReadModel, right: RuleReadModel): number {
  return left.category.localeCompare(right.category)
    || left.subcategory.localeCompare(right.subcategory)
    || left.title.localeCompare(right.title)
    || left.id.localeCompare(right.id);
}

function projectBundledRule(value: unknown): RuleReadModel | null {
  const rule = object(value);
  const index = projectCatalogRuleSummary({
    qualifiedId: rule?.id,
    name: rule?.title,
    kind: "entity",
    status: "active",
    path: rule?.path,
    contentFingerprint: rule?.contentFingerprint,
  });
  const subcategory = boundedText(rule?.subcategory, 500) ?? "";
  const summary = boundedText(rule?.summary, 2_000);
  const revision = rule?.revision;
  const source = object(rule?.source);
  const locator = boundedText(source?.locator, 500);
  if (
    !index || index.category !== rule?.category || index.subcategory !== subcategory || !summary
    || !Number.isInteger(revision) || Number(revision) < 1 || Number(revision) > 1_000_000
    || source?.id !== SOURCE_ID || !locator
  ) return null;
  return {
    ...index,
    summary,
    revision: Number(revision),
    source: { id: SOURCE_ID, locator },
  };
}

async function readBundledRulesReference(origin: string, fetchImpl: typeof fetch): Promise<RuleReadModel[]> {
  try {
    const response = await fetchImpl(new URL("/ui/dnd2024-play/assets/rules-catalog.json", `${origin}/`), {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    if (!response.ok) return [];
    const payload = await response.json();
    if (!Array.isArray(payload) || payload.length > MAXIMUM_RECORDS) return [];
    const rules = payload.map(projectBundledRule);
    if (rules.some((rule) => rule === null)) return [];
    const projected = rules as RuleReadModel[];
    if (new Set(projected.map((rule) => rule.id)).size !== projected.length) return [];
    return projected.sort(compareRules);
  } catch {
    return [];
  }
}

export async function readRulesReference({
  serverOrigin,
  applicationId,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  fetchImpl?: typeof fetch;
}): Promise<RuleReadModel[]> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID) return [];

  const branches = [ENTITY_BRANCH];
  const queuedBranches = new Set(branches);
  const visitedBranches = new Set<string>();
  const rules = new Map<string, RuleReadModel>();
  let pageCount = 0;

  try {
    while (branches.length > 0) {
      const branch = branches.shift();
      if (!branch || visitedBranches.has(branch)) throw new Error("Catalog branch repeated.");
      visitedBranches.add(branch);
      if (visitedBranches.size > MAXIMUM_NODES) throw new Error("Catalog node bound exceeded.");

      let cursor: string | null = null;
      const visitedCursors = new Set<string>();
      do {
        pageCount += 1;
        if (pageCount > MAXIMUM_PAGES) throw new Error("Catalog page bound exceeded.");
        if (cursor && visitedCursors.has(cursor)) throw new Error("Catalog cursor repeated.");
        if (cursor) visitedCursors.add(cursor);

        const requestUrl = new URL(`/api/applications/${APPLICATION_ID}/catalog/browse`, `${origin}/`);
        requestUrl.searchParams.set("collection", COLLECTION_ID);
        requestUrl.searchParams.set("branch", branch);
        requestUrl.searchParams.set("limit", String(PAGE_SIZE));
        if (cursor) requestUrl.searchParams.set("cursor", cursor);
        const response = await fetchImpl(requestUrl, {
          headers: { Accept: "application/json" },
          cache: "no-store",
        });
        if (!response.ok) throw new Error("Catalog browse unavailable.");
        const page = object(await response.json());
        const entries = Array.isArray(page?.entries) ? page.entries : null;
        if (!entries || entries.length > PAGE_SIZE) throw new Error("Catalog page invalid.");

        for (const candidate of entries) {
          const entry = object(candidate);
          if (!entry) throw new Error("Catalog entry invalid.");
          if (isNodeEntry(entry)) {
            const node = object(entry.node);
            const childPath = boundedText(node?.path, 500);
            if (!childPath || !childPath.startsWith(`${branch}/`) || queuedBranches.has(childPath)) {
              throw new Error("Catalog node invalid.");
            }
            queuedBranches.add(childPath);
            branches.push(childPath);
          } else if (isRecordEntry(entry)) {
            const rule = projectCatalogRuleSummary(entry.record);
            if (rule) {
              if (rules.has(rule.id)) throw new Error("Catalog rule repeated.");
              rules.set(rule.id, rule);
              if (rules.size > MAXIMUM_RECORDS) throw new Error("Catalog record bound exceeded.");
            }
          } else {
            throw new Error("Catalog entry kind invalid.");
          }
        }

        cursor = page?.nextCursor === null || page?.nextCursor === undefined
          ? null
          : boundedText(page.nextCursor, 4_096);
        if (page?.nextCursor !== null && page?.nextCursor !== undefined && !cursor) {
          throw new Error("Catalog cursor invalid.");
        }
      } while (cursor);
    }
  } catch {
    return readBundledRulesReference(origin, fetchImpl);
  }

  return rules.size > 0
    ? [...rules.values()].sort(compareRules)
    : readBundledRulesReference(origin, fetchImpl);
}

export async function readRuleReferenceDetail({
  serverOrigin,
  applicationId,
  rule,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  rule: RuleReadModel;
  fetchImpl?: typeof fetch;
}): Promise<RuleReadModel | null> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  const validatedRule = projectCatalogRuleSummary({
    qualifiedId: rule.id,
    name: rule.title,
    kind: "entity",
    status: "active",
    path: rule.path,
    contentFingerprint: rule.contentFingerprint,
  });
  if (!origin || applicationId !== APPLICATION_ID || !validatedRule) return null;

  try {
    const requestUrl = new URL(
      `/api/applications/${APPLICATION_ID}/catalog/records/${encodeURIComponent(rule.id)}`,
      `${origin}/`,
    );
    requestUrl.searchParams.set("collection", COLLECTION_ID);
    const response = await fetchImpl(requestUrl, {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    return response.ok ? projectCatalogRuleRecord(await response.json(), rule) : null;
  } catch {
    return null;
  }
}
