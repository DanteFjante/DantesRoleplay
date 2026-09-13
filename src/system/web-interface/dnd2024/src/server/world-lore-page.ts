import type { ConnectedCampaignEnvelope, WorldLoreEntry } from "../data/hub-types";
import { LORE_CATEGORIES, type LorePageRequest, type WorldLorePage } from "../data/world-lore-page";
import { ViewReadError } from "../data/view-read-client";
import { readRegisteredPartyKnowledgePage } from "./party-knowledge.js";

/** One authorized page; it neither follows continuations nor fetches per-entry entities or media. */
export async function readWorldLorePage({ source, request, origin, fetchImpl = fetch }: {
  source: ConnectedCampaignEnvelope; request: LorePageRequest; origin: string; fetchImpl?: typeof fetch;
}): Promise<WorldLorePage> {
  const perspective = source.audience.perspective ?? source.audience.seat;
  const browse = perspective === "dm";
  if (!browse && (request.category || request.kind || request.query))
    throw new ViewReadError("incompatible-data", "Filtered browsing is unavailable for this perspective.");
  const page = await readRegisteredPartyKnowledgePage({ fetchImpl, origin,
    applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
    campaignId: source.campaign.id, worldId: source.contextSelection.selectedWorldId,
    perspective, browse, category: request.category, kind: request.kind, query: request.query,
    cursor: request.cursor, expectedSourceRevision: request.expectedSourceRevision ?? null,
    expectedGraphSourceRevision: request.expectedGraphRevision ?? null,
    expectedSelectionFingerprint: request.expectedSelectionFingerprint ?? null,
  });
  if (!page || !["ready", "empty"].includes(page.status))
    throw new ViewReadError("incompatible-data", "This lore page could not be read.");
  if (source.campaign.projection?.resolutionFingerprint &&
      source.campaign.projection.resolutionFingerprint !== page.projection.resolutionFingerprint)
    throw new ViewReadError("stale-data", "The table binding changed before this lore page was read.");
  const seen = new Set<string>();
  const entries: WorldLoreEntry[] = [];
  for (const entry of page.entries) {
    const id = entry.knowledgeId ?? entry.recognitionKey;
    if (!id || seen.has(id)) throw new ViewReadError("incompatible-data", "This lore page contains duplicate identities.");
    seen.add(id);
    const subject = entry.subject;
    const location = (source.locationDirectory ?? []).find((item) => item.id === subject?.id);
    entries.push({ id, title: entry.title ?? subject?.name ?? entry.text,
      category: LORE_CATEGORIES[entry.subjectKind ?? ""] ?? "Known subjects",
      status: [entry.knowledgeKind, entry.stance].filter(Boolean).join(" · "),
      summary: entry.summary ?? entry.text, body: entry.text,
      linkedLocations: location ? [{ id: location.id, name: location.name }] : [],
      linkedPeople: [], linkedFactions: [], linkedHistory: [],
      ...(entry.admissions ? { admissions: entry.admissions } : {}),
    });
  }
  return { entries, totalCount: browse ? page.totalCount : null,
    nextCursor: page.nextCursor, sourceRevision: page.sourceRevisionFingerprint,
    graphRevision: page.graphSourceRevision, selectionFingerprint: browse ? page.selectionFingerprint : null,
    facets: browse && page.facets ? page.facets as WorldLorePage["facets"] : { category: {}, kind: {} },
    coverage: page.coverage === "partial" ? "partial" : "complete", filterable: browse,
  };
}
