import type { RuleReadModel, RulesReferencePublication } from "../data/hub-types";
import { RESOURCE_FRESHNESS_MS, resourceCacheKey } from "../data/resource-policy.ts";
import { ResourceStore, type ResourceInvalidationReason, type ResourceStoreMetrics } from "../data/resource-store.ts";
import { ViewReadError } from "../data/view-read-client.ts";
import { normalizeGameServerOrigin } from "./game-server-context.js";
import { readBoundedJson } from "./read-model-response.js";

const APPLICATION_ID = "dnd2024";
const MAXIMUM_SECTIONS = 128;
const MAXIMUM_RULES = 4_096;
const MAXIMUM_RESPONSE_BYTES = 2_097_152;
const CONTENT_KIND = /^[a-z][a-z0-9-]{0,62}$/u;

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
  const rawRelatedContent = Array.isArray(rule?.relatedContent) && rule.relatedContent.length <= 32
    ? rule.relatedContent : null;
  if (!id || !resolutionKey || !title || !summary || order === null || !ownerId || !sourceLabel
    || !["core", "homebrew", "compatibility", "third-party"].includes(classification ?? "")
    || !["public", "dm"].includes(visibility ?? "")
    || !relatedRuleIds || !mechanicIds || !procedureIds || !rawRelatedContent
    || mechanicIds.length + procedureIds.length === 0) return null;

  const relatedContent: RuleReadModel["relatedContent"] = [];
  for (const candidate of rawRelatedContent) {
    const link = object(candidate);
    const kind = text(link?.kind, 63);
    const entityId = text(link?.entityId, 400);
    const linkTitle = text(link?.title, 400);
    const collection = link?.collection === null ? null : text(link?.collection, 63);
    const contentFingerprint = link?.contentFingerprint === null
      ? null : text(link?.contentFingerprint, 64)?.toUpperCase() ?? null;
    if (!kind || !CONTENT_KIND.test(kind) || !entityId || !linkTitle
      || (link?.collection !== null && !collection)
      || (link?.contentFingerprint !== null
        && (!contentFingerprint || !/^[A-F0-9]{64}$/u.test(contentFingerprint)))
      || typeof link?.available !== "boolean"
      || link.available !== Boolean(collection && contentFingerprint)) return null;
    relatedContent.push({ kind, entityId, title: linkTitle, collection, contentFingerprint,
      available: link.available });
  }
  if (new Set(relatedContent.map((link) => `${link.kind}\n${link.entityId}`)).size !== relatedContent.length)
    return null;

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
    relatedContent,
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

export function projectRulesPublication(value: unknown): RulesReferencePublication | null {
  const envelope = object(value);
  const resolutionFingerprint = text(envelope?.resolutionFingerprint, 128);
  const rulesFingerprint = text(envelope?.rulesFingerprint, 128);
  const articleCount = integer(envelope?.articleCount);
  if (envelope?.applicationId !== APPLICATION_ID || !resolutionFingerprint || !rulesFingerprint
    || articleCount === null || articleCount > MAXIMUM_RULES
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
  if (new Set(rules.map((rule) => rule.id)).size !== rules.length || articleCount !== rules.length) return null;
  rules.sort((left, right) => left.section.order - right.section.order
    || left.section.label.localeCompare(right.section.label)
    || left.section.id.localeCompare(right.section.id)
    || left.order - right.order
    || left.title.localeCompare(right.title)
    || left.id.localeCompare(right.id));
  return {
    applicationId: APPLICATION_ID,
    resolutionFingerprint,
    rulesFingerprint,
    audience: envelope.audience as RulesReferencePublication["audience"],
    articleCount,
    rules,
  };
}

export function projectResolvedRules(value: unknown): RuleReadModel[] | null {
  return projectRulesPublication(value)?.rules ?? null;
}

export async function readRulesReference({
  serverOrigin,
  applicationId,
  signal,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  signal?: AbortSignal;
  fetchImpl?: typeof fetch;
}): Promise<RulesReferencePublication> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID)
    throw new ViewReadError("incompatible-data", "Published rules are unavailable.");
  const response = await fetchImpl(new URL(`/api/applications/${APPLICATION_ID}/rules`, `${origin}/`), {
    headers: { Accept: "application/json" },
    cache: "no-store",
    signal,
  });
  if (!response.ok)
    throw new ViewReadError("transport", "Published rules could not be loaded.");
  const decoded = await readBoundedJson(response, MAXIMUM_RESPONSE_BYTES);
  const publication = decoded.status === "ready" ? projectRulesPublication(decoded.value) : null;
  if (!publication)
    throw new ViewReadError("incompatible-data", "The published-rules response did not match its contract.");
  return publication;
}

export class RulesReferenceClient {
  readonly #store: ResourceStore;
  readonly #publication;

  constructor({ serverOrigin, applicationId = APPLICATION_ID, fetchImpl = fetch }: {
    serverOrigin: string;
    applicationId?: string;
    fetchImpl?: typeof fetch;
  }) {
    this.#store = new ResourceStore({ maximumEntries: 4, maximumRetainedBytes: 4 * 1024 * 1024,
      diagnosticName: "rules-reference-resources" });
    this.#publication = this.#store.define<string, RulesReferencePublication>({
      name: "rules-reference-publication",
      cacheKey: () => resourceCacheKey("rules-reference-v2", applicationId),
      read: (_, signal) => readRulesReference({ serverOrigin, applicationId, signal, fetchImpl }),
      validate: (value): value is RulesReferencePublication => projectRulesPublication({
        ...(value as RulesReferencePublication),
        sections: publicationSections((value as RulesReferencePublication)?.rules),
      }) !== null,
      maximumAgeMs: RESOURCE_FRESHNESS_MS.rulesReference,
      maximumEntryBytes: MAXIMUM_RESPONSE_BYTES,
    });
  }

  async load(signal?: AbortSignal, preferCached = true) {
    return (await this.#publication.load(APPLICATION_ID, { signal, preferCached })).value;
  }

  invalidate(reason: ResourceInvalidationReason = "manual") { this.#publication.invalidate(undefined, reason); }
  metrics(): ResourceStoreMetrics { return this.#store.metrics(); }
}

function publicationSections(rules: RuleReadModel[] | undefined) {
  if (!Array.isArray(rules)) return null;
  const sections = new Map<string, { id: string; label: string; order: number; rules: RuleReadModel[] }>();
  for (const rule of rules) {
    const section = sections.get(rule.section.id) ?? { ...rule.section, rules: [] };
    section.rules.push(rule);
    sections.set(rule.section.id, section);
  }
  return [...sections.values()];
}
