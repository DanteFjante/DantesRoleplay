import type { RuleReadModel } from "../data/hub-types";
import { normalizeGameServerOrigin } from "./game-server-context.js";

const APPLICATION_ID = "dnd2024";
const MAXIMUM_SECTIONS = 128;
const MAXIMUM_RULES = 4_096;

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function text(value: unknown, maximum: number): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value
    && !Array.from(value).some((character) => /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/u.test(character))
    ? value
    : null;
}

function integer(value: unknown): number | null {
  return Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 10_000
    ? Number(value)
    : null;
}

function textArray(value: unknown, maximumItems: number, maximumText: number): string[] | null {
  if (!Array.isArray(value) || value.length > maximumItems) return null;
  const values = value.map((item) => text(item, maximumText));
  if (values.some((item) => item === null)) return null;
  const result = values as string[];
  return new Set(result).size === result.length ? result : null;
}

function projectRule(value: unknown, section: RuleReadModel["section"]): RuleReadModel | null {
  const rule = object(value);
  const source = object(rule?.source);
  const authority = object(rule?.authority);
  const id = text(rule?.id, 400);
  const resolutionKey = text(rule?.resolutionKey, 400);
  const title = text(rule?.title, 200);
  const summary = text(rule?.summary, 2_000);
  const order = integer(rule?.order);
  const ownerId = text(source?.ownerId, 200);
  const sourceLabel = text(source?.label, 200);
  const classification = text(source?.classification, 40);
  const visibility = text(rule?.visibility, 20);
  const relatedRuleIds = textArray(rule?.relatedRuleIds, 32, 400);
  const mechanicIds = textArray(authority?.mechanicIds, 32, 400);
  const procedureIds = textArray(authority?.procedureIds, 32, 400);
  if (!id || !resolutionKey || !title || !summary || order === null || !ownerId || !sourceLabel
    || !["core", "homebrew", "compatibility", "third-party"].includes(classification ?? "")
    || !["public", "dm"].includes(visibility ?? "")
    || !relatedRuleIds || !mechanicIds || !procedureIds
    || mechanicIds.length + procedureIds.length === 0) return null;

  const rawBlocks = Array.isArray(rule?.blocks) && rule.blocks.length > 0 && rule.blocks.length <= 64
    ? rule.blocks
    : null;
  if (!rawBlocks) return null;
  const blocks: RuleReadModel["blocks"] = [];
  for (const candidate of rawBlocks) {
    const block = object(candidate);
    const kind = text(block?.kind, 20);
    const heading = block?.heading === null ? null : text(block?.heading, 200);
    const body = block?.body === null ? null : text(block?.body, 10_000);
    const items = textArray(block?.items, 64, 1_000);
    if (!["paragraph", "steps", "list", "callout"].includes(kind ?? "")
      || (block?.heading !== null && !heading)
      || (block?.body !== null && !body)
      || !items || (!body && items.length === 0)
      || ((kind === "steps" || kind === "list") && items.length === 0)) return null;
    blocks.push({ kind: kind as RuleReadModel["blocks"][number]["kind"], heading, body, items });
  }

  const rawExamples = Array.isArray(rule?.examples) && rule.examples.length <= 32 ? rule.examples : null;
  const examples: RuleReadModel["examples"] = [];
  if (!rawExamples) return null;
  for (const candidate of rawExamples) {
    const example = object(candidate);
    const exampleTitle = text(example?.title, 200);
    const body = text(example?.body, 5_000);
    if (!exampleTitle || !body) return null;
    examples.push({ title: exampleTitle, body });
  }

  const rawCitations = Array.isArray(rule?.citations) && rule.citations.length > 0 && rule.citations.length <= 32
    ? rule.citations
    : null;
  const citations: RuleReadModel["citations"] = [];
  if (!rawCitations) return null;
  for (const candidate of rawCitations) {
    const citation = object(candidate);
    const sourceId = text(citation?.sourceId, 200);
    const locator = text(citation?.locator, 1_000);
    if (!sourceId || !locator) return null;
    citations.push({ sourceId, locator });
  }

  return {
    id,
    resolutionKey,
    title,
    summary,
    order,
    section,
    blocks,
    examples,
    relatedRuleIds,
    citations,
    authority: { mechanicIds, procedureIds },
    visibility: visibility as RuleReadModel["visibility"],
    source: {
      ownerId,
      label: sourceLabel,
      classification: classification as RuleReadModel["source"]["classification"],
    },
  };
}

export function projectResolvedRules(value: unknown): RuleReadModel[] | null {
  const envelope = object(value);
  const resolutionFingerprint = text(envelope?.resolutionFingerprint, 128);
  const rulesFingerprint = text(envelope?.rulesFingerprint, 128);
  if (envelope?.applicationId !== APPLICATION_ID || !resolutionFingerprint || !rulesFingerprint
    || !["public", "dm"].includes(String(envelope?.audience))) return null;
  const rawSections = Array.isArray(envelope?.sections) && envelope.sections.length <= MAXIMUM_SECTIONS
    ? envelope.sections
    : null;
  if (!rawSections) return null;

  const rules: RuleReadModel[] = [];
  const sectionIds = new Set<string>();
  for (const candidate of rawSections) {
    const section = object(candidate);
    const id = text(section?.id, 100);
    const label = text(section?.label, 160);
    const order = integer(section?.order);
    const rawRules = Array.isArray(section?.rules) ? section.rules : null;
    if (!id || !label || order === null || !rawRules || sectionIds.has(id)) return null;
    sectionIds.add(id);
    for (const rawRule of rawRules) {
      if (rules.length === MAXIMUM_RULES) return null;
      const rule = projectRule(rawRule, { id, label, order });
      if (!rule) return null;
      rules.push(rule);
    }
  }
  if (new Set(rules.map((rule) => rule.id)).size !== rules.length) return null;
  return rules.sort((left, right) => left.section.order - right.section.order
    || left.section.label.localeCompare(right.section.label)
    || left.section.id.localeCompare(right.section.id)
    || left.order - right.order
    || left.title.localeCompare(right.title)
    || left.id.localeCompare(right.id));
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
  try {
    const response = await fetchImpl(new URL(`/api/applications/${APPLICATION_ID}/rules`, `${origin}/`), {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    if (!response.ok) return [];
    return projectResolvedRules(await response.json()) ?? [];
  } catch {
    return [];
  }
}
