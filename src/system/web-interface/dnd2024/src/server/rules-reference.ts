import type { RuleCategory, RuleReadModel } from "../data/hub-types";
import { normalizeGameServerOrigin } from "./game-server-context.js";

const APPLICATION_ID = "dnd2024";
const COLLECTION_ID = "dnd2024";
const SOURCE_ID = "source.dnd2024.srd-5.2.1";
const ACTIVITY_ARCHETYPE = "dnd2024.archetype.activity-definition";

export const REVIEWED_RULE_REFERENCE_IDS = Object.freeze([
  "dnd2024.shared.action.attack",
  "dnd2024.shared.action.dash",
  "dnd2024.shared.action.disengage",
  "dnd2024.shared.action.dodge",
  "dnd2024.shared.action.help",
  "dnd2024.shared.action.hide",
  "dnd2024.shared.action.influence",
  "dnd2024.shared.action.magic",
  "dnd2024.shared.action.ready",
  "dnd2024.shared.action.search",
  "dnd2024.shared.action.study",
  "dnd2024.shared.action.utilize",
  "dnd2024.shared.action.opportunity-attack",
  "dnd2024.shared.action.unarmed-strike",
]);

function boundedText(value: unknown, maximum: number): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value && !Array.from(value).some((character) => /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/u.test(character))
    ? value
    : null;
}

function recordContent(value: unknown): Record<string, unknown> | null {
  if (typeof value !== "string" || value.length === 0 || value.length > 100_000) return null;
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

export function projectCatalogRuleRecord(value: unknown, expectedId: string): RuleReadModel | null {
  const envelope = object(value);
  const routeSummary = object(envelope?.summary);
  if (routeSummary?.qualifiedId !== expectedId || routeSummary.status !== "active") return null;

  const record = recordContent(envelope?.contentJson);
  if (!record || record.id !== expectedId || record.archetype !== ACTIVITY_ARCHETYPE) return null;
  const title = boundedText(record.name, 200);
  const components = object(record.components);
  const version = object(components?.["dnd2024.core.version"]);
  const presentation = object(components?.["dnd2024.core.presentation"]);
  const activation = object(components?.["dnd2024.activity.activation"]);
  const source = object(components?.["dnd2024.core.source"]);
  const citations = Array.isArray(source?.citations) ? source.citations : [];
  if (!title || version?.revision !== 1 || version.status !== "active" || citations.length !== 1) return null;

  const summary = boundedText(presentation?.summary, 2_000);
  const economy = activation?.economy;
  if (economy !== "action" && economy !== "reaction") return null;
  const category: RuleCategory = economy === "action" ? "Action" : "Reaction";
  const citation = object(citations[0]);
  const sourceReference = object(citation?.sourceRef);
  const locator = boundedText(citation?.locator, 500);
  if (!summary || sourceReference?.entityId !== SOURCE_ID || !locator) return null;

  return {
    id: expectedId,
    title,
    category,
    summary,
    source: { id: SOURCE_ID, locator },
  };
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

  const records = await Promise.all(REVIEWED_RULE_REFERENCE_IDS.map(async (id) => {
    const path = `/api/applications/${APPLICATION_ID}/catalog/records/${encodeURIComponent(id)}`;
    const requestUrl = new URL(path, `${origin}/`);
    requestUrl.searchParams.set("collection", COLLECTION_ID);
    try {
      const response = await fetchImpl(requestUrl, {
        headers: { Accept: "application/json" },
        cache: "no-store",
      });
      if (!response.ok) return null;
      return projectCatalogRuleRecord(await response.json(), id);
    } catch {
      return null;
    }
  }));

  return records.filter((record): record is RuleReadModel => record !== null);
}
