import type { RuleReadModel, RulesReferencePublication } from "../data/hub-types";
import { RequestCoordinator } from "../data/request-coordinator.ts";
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

function retainedTextArray(value: unknown, maximumItems: number, maximumText: number) {
  if (!Array.isArray(value) || value.length > maximumItems)
    return { values: [] as string[], status: "unavailable" as const };
  const values: string[] = [];
  const seen = new Set<string>();
  let partial = false;
  for (const item of value) {
    const candidate = text(item, maximumText);
    if (!candidate || seen.has(candidate)) { partial = true; continue; }
    seen.add(candidate); values.push(candidate);
  }
  return { values, status: partial ? "partial" as const : "ready" as const };
}

function fingerprint(value: unknown, allowNoResolution = false): string | null {
  if (allowNoResolution && value === "none") return value;
  const candidate = text(value, 64);
  return candidate && /^[A-F0-9]{64}$/iu.test(candidate) ? candidate.toUpperCase() : null;
}

function projectRule(value: unknown, section: RuleReadModel["section"], sectionAvailability: { label: boolean; order: boolean }): RuleReadModel | null {
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
  const relatedRuleIds = retainedTextArray(rule?.relatedRuleIds, 32, 400);
  const mechanicIds = retainedTextArray(authority?.mechanicIds, 32, 400);
  const procedureIds = retainedTextArray(authority?.procedureIds, 32, 400);
  const rawRelatedContent = Array.isArray(rule?.relatedContent) && rule.relatedContent.length <= 32
    ? rule.relatedContent : [];
  if (!id || !resolutionKey || !ownerId
    || !["public", "dm"].includes(visibility ?? "")) return null;

  const relatedContent: RuleReadModel["relatedContent"] = [];
  let relatedContentPartial = !Array.isArray(rule?.relatedContent) || rule.relatedContent.length > 32;
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
      || link.available !== Boolean(collection && contentFingerprint)) { relatedContentPartial = true; continue; }
    if (relatedContent.some((existing) => existing.kind === kind && existing.entityId === entityId)) {
      relatedContentPartial = true;
      continue;
    }
    relatedContent.push({ kind, entityId, title: linkTitle, collection, contentFingerprint,
      available: link.available });
  }

  const rawBlocks = Array.isArray(rule?.blocks) && rule.blocks.length > 0 && rule.blocks.length <= 64
    ? rule.blocks : [];
  const blocks: RuleReadModel["blocks"] = [];
  let blocksPartial = !Array.isArray(rule?.blocks) || rule.blocks.length === 0 || rule.blocks.length > 64;
  for (const candidate of rawBlocks) {
    const block = object(candidate);
    const kind = text(block?.kind, 20);
    const heading = block?.heading === null || block?.heading === undefined ? null : text(block?.heading, 200);
    const body = block?.body === null || block?.body === undefined ? null : text(block?.body, 10_000);
    const items = retainedTextArray(block?.items, 64, 1_000);
    if (!["paragraph", "steps", "list", "callout"].includes(kind ?? "")
      || (!body && items.values.length === 0)) { blocksPartial = true; continue; }
    if ((block?.heading !== null && block?.heading !== undefined && !heading)
      || (block?.body !== null && block?.body !== undefined && !body)
      || items.status !== "ready") blocksPartial = true;
    blocks.push({ kind: kind as RuleReadModel["blocks"][number]["kind"], heading, body, items: items.values });
  }

  const rawExamples = Array.isArray(rule?.examples) && rule.examples.length <= 32 ? rule.examples : [];
  const examples: RuleReadModel["examples"] = [];
  let examplesPartial = !Array.isArray(rule?.examples) || rule.examples.length > 32;
  for (const candidate of rawExamples) {
    const example = object(candidate);
    const exampleTitle = text(example?.title, 200);
    const body = text(example?.body, 5_000);
    if (!body) { examplesPartial = true; continue; }
    if (!exampleTitle) examplesPartial = true;
    examples.push({ title: exampleTitle ?? "Unnamed example", body });
  }

  const rawCitations = Array.isArray(rule?.citations) && rule.citations.length > 0 && rule.citations.length <= 32
    ? rule.citations : [];
  const citations: RuleReadModel["citations"] = [];
  let citationsPartial = !Array.isArray(rule?.citations) || rule.citations.length === 0 || rule.citations.length > 32;
  for (const candidate of rawCitations) {
    const citation = object(candidate);
    const sourceId = text(citation?.sourceId, 200);
    const locator = text(citation?.locator, 1_000);
    if (!sourceId || !locator) { citationsPartial = true; continue; }
    citations.push({ sourceId, locator });
  }

  const fieldStatus: NonNullable<RuleReadModel["fieldStatus"]> = {};
  if (!title) fieldStatus.title = "unavailable";
  if (order === null) fieldStatus.order = "unavailable";
  if (!sectionAvailability.label) fieldStatus.sectionLabel = "unavailable";
  if (!sectionAvailability.order) fieldStatus.sectionOrder = "unavailable";
  if (!summary) fieldStatus.summary = "unavailable";
  if (blocksPartial) fieldStatus.blocks = blocks.length ? "partial" : "unavailable";
  if (examplesPartial) fieldStatus.examples = examples.length ? "partial" : "unavailable";
  if (relatedRuleIds.status !== "ready") fieldStatus.relatedRules = relatedRuleIds.values.length ? "partial" : "unavailable";
  if (relatedContentPartial) fieldStatus.relatedContent = relatedContent.length ? "partial" : "unavailable";
  if (citationsPartial) fieldStatus.citations = citations.length ? "partial" : "unavailable";
  if (mechanicIds.status !== "ready" || procedureIds.status !== "ready")
    fieldStatus.authority = mechanicIds.values.length + procedureIds.values.length ? "partial" : "unavailable";
  if (!sourceLabel) fieldStatus.sourceLabel = "unavailable";
  if (!["core", "homebrew", "compatibility", "third-party"].includes(classification ?? ""))
    fieldStatus.sourceClassification = "unavailable";

  return {
    id,
    resolutionKey,
    title: title ?? "Unnamed rule",
    summary: summary ?? "Summary unavailable.",
    order,
    section,
    blocks,
    examples,
    relatedRuleIds: relatedRuleIds.values,
    relatedContent,
    citations,
    authority: { mechanicIds: mechanicIds.values, procedureIds: procedureIds.values },
    visibility: visibility as RuleReadModel["visibility"],
    source: {
      ownerId,
      label: sourceLabel ?? ownerId,
      classification: ["core", "homebrew", "compatibility", "third-party"].includes(classification ?? "")
        ? classification as RuleReadModel["source"]["classification"] : "unknown",
    },
    fieldStatus,
  };
}

export function projectRulesPublication(value: unknown): RulesReferencePublication | null {
  const envelope = object(value);
  const resolutionFingerprint = fingerprint(envelope?.resolutionFingerprint, true);
  const rulesFingerprint = fingerprint(envelope?.rulesFingerprint);
  const articleCount = integer(envelope?.articleCount);
  if (envelope?.applicationId !== APPLICATION_ID || !resolutionFingerprint || !rulesFingerprint
    || !["public", "dm"].includes(String(envelope?.audience))) return null;
  const rawSections = Array.isArray(envelope?.sections) && envelope.sections.length <= MAXIMUM_SECTIONS
    ? envelope.sections
    : null;
  if (!rawSections) return null;

  const rules: RuleReadModel[] = [];
  const notices: string[] = [];
  const sectionIds = new Set<string>();
  for (const candidate of rawSections) {
    const section = object(candidate);
    const id = text(section?.id, 100);
    const label = text(section?.label, 160);
    const order = integer(section?.order);
    const rawRules = Array.isArray(section?.rules) ? section.rules : null;
    if (!id || !rawRules || sectionIds.has(id)) { notices.push("A malformed rules section was omitted."); continue; }
    sectionIds.add(id);
    if (!label) notices.push(`Section ${id} has no readable label.`);
    for (const rawRule of rawRules) {
      if (rules.length === MAXIMUM_RULES) { notices.push("The rules response exceeded its display limit."); break; }
      const rule = projectRule(rawRule, { id, label: label ?? id, order }, { label: Boolean(label), order: order !== null });
      if (!rule || rules.some((existing) => existing.id === rule.id)) { notices.push("A malformed or duplicate rule was omitted."); continue; }
      rules.push(rule);
    }
  }
  const compareOrder = (left: number | null, right: number | null) => left === null ? right === null ? 0 : 1
    : right === null ? -1 : left - right;
  rules.sort((left, right) => compareOrder(left.section.order, right.section.order)
    || left.section.label.localeCompare(right.section.label)
    || left.section.id.localeCompare(right.section.id)
    || compareOrder(left.order, right.order)
    || left.title.localeCompare(right.title)
    || left.id.localeCompare(right.id));
  // A public publication cannot carry a DM-only rule under a public envelope.
  // The owner uses the publication audience to bind player access, so accepting
  // an internally contradictory response would widen that authority boundary.
  if (envelope.audience === "public" && rules.some((rule) => rule.visibility !== "public")) return null;
  return {
    applicationId: APPLICATION_ID,
    resolutionFingerprint,
    rulesFingerprint,
    audience: envelope.audience as RulesReferencePublication["audience"],
    articleCount: articleCount === null || articleCount > MAXIMUM_RULES || articleCount !== rules.length
      ? null : articleCount,
    rules,
    coverage: notices.length || articleCount === null || articleCount > MAXIMUM_RULES || articleCount !== rules.length ||
      rules.some((rule) => Object.keys(rule.fieldStatus ?? {}).length) ? "partial" : "ready",
    notices: articleCount === null || articleCount > MAXIMUM_RULES || articleCount !== rules.length
      ? [...notices, "Published rule count is unavailable; displayed records were counted locally."] : notices,
  };
}

export function projectResolvedRules(value: unknown): RuleReadModel[] | null {
  return projectRulesPublication(value)?.rules ?? null;
}

function isRulesPublication(value: unknown): value is RulesReferencePublication {
  const publication = object(value);
  return Boolean(publication?.applicationId === APPLICATION_ID && fingerprint(publication.resolutionFingerprint, true)
    && fingerprint(publication.rulesFingerprint) && ["public", "dm"].includes(String(publication.audience))
    && (publication.articleCount === null || Number.isSafeInteger(publication.articleCount) && Number(publication.articleCount) >= 0)
    && Array.isArray(publication.rules) && publication.rules.length <= MAXIMUM_RULES);
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
    throw new ViewReadError(response.status === 401 || response.status === 403 ? "authorization" : "transport",
      response.status === 401 || response.status === 403
        ? "Published rules are not available for this audience."
        : "Published rules could not be loaded.");
  const decoded = await readBoundedJson(response, MAXIMUM_RESPONSE_BYTES);
  const publication = decoded.status === "ready" ? projectRulesPublication(decoded.value) : null;
  if (!publication)
    throw new ViewReadError("incompatible-data", "The published-rules response did not match its contract.");
  return publication;
}

export class RulesReferenceClient {
  readonly #coordinator = new RequestCoordinator();
  readonly #serverOrigin: string;
  readonly #applicationId: string;
  readonly #fetchImpl: typeof fetch;

  constructor({ serverOrigin, applicationId = APPLICATION_ID, fetchImpl = fetch }: {
    serverOrigin: string;
    applicationId?: string;
    fetchImpl?: typeof fetch;
  }) {
    this.#serverOrigin = serverOrigin;
    this.#applicationId = applicationId;
    this.#fetchImpl = fetchImpl;
  }

  async load(signal?: AbortSignal, _preferCached = true) {
    return this.#coordinator.load({
      key: () => `rules-reference-v3\u0000${this.#applicationId}`,
      read: (_, activeSignal) => readRulesReference({ serverOrigin: this.#serverOrigin,
        applicationId: this.#applicationId, signal: activeSignal, fetchImpl: this.#fetchImpl }),
      validate: isRulesPublication,
      maximumBytes: MAXIMUM_RESPONSE_BYTES,
    }, APPLICATION_ID, signal);
  }

  invalidate() { this.#coordinator.invalidate(); }
}
