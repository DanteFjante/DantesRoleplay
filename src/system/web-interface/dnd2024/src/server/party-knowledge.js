import { readModelResponse } from "./read-model-response.js";

const query = { id: "dnd2024.query.party-knowledge" };
const contentStances = new Set(["known", "suspected", "believed", "doubted", "disbelieved"]);
const memberStances = new Set([...contentStances, "familiar", "unknown"]);
const maximumPageBodyBytes = 65_536;
const maximumDataBytes = 60_000;
const maximumSourceEntries = 2_048;
const maximumPages = 256;
// readModelResponse enforces the body cap while consuming the stream but does not expose its
// measured byte count. Charging every page at the full cap makes this a conservative 16 MiB
// cumulative transport bound (256 * 65,536) even when Content-Length is absent or dishonest.
const maximumCumulativeBodyBytes = 16 * 1024 * 1024;
const object = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
const own = (value, field) => object(value) && Object.hasOwn(value, field) ? value[field] : undefined;
const token = (value) => typeof value === "string" && value.length > 0 && value.length <= 200 &&
  value === value.trim() && !/\s/u.test(value) ? value : null;
const text = (value, maximum) => typeof value === "string" && value.trim().length > 0 && value.length <= maximum ? value : null;
const entryIdentity = (entry) => entry.knowledgeId
  ? `knowledge:${entry.knowledgeId}`
  : `recognition:${entry.recognitionKey}`;

/** Read only the fields this view uses; safe neighbors survive malformed display fields. */
export function projectPartyKnowledge(value, { campaignId, worldId, perspective }, { ignorePagingCoverage = false } = {}) {
  const audience = perspective === "dm" ? "dm" : "party";
  if (own(value, "campaignId") !== campaignId || own(value, "worldId") !== worldId ||
      own(value, "audience") !== audience || !["ready", "empty"].includes(own(value, "status"))) return null;
  const rawEntries = own(value, "entries");
  const rawLocations = own(value, "locations");
  if (Array.isArray(rawEntries) && rawEntries.length > 200 ||
      Array.isArray(rawLocations) && rawLocations.length > 100) return null;
  if (value.status === "empty" && (!Array.isArray(rawEntries) || !Array.isArray(rawLocations) ||
      rawEntries.length !== 0 || rawLocations.length !== 0)) return null;
  let partial = (!ignorePagingCoverage && own(value, "coverage") === "partial") ||
    !Array.isArray(rawEntries) || !Array.isArray(rawLocations);
  const entries = (rows) => {
    if (!Array.isArray(rows) || rows.length > 200) { partial = true; return []; }
    const projected = rows.flatMap((entry) => {
      const stance = own(entry, "stance");
      const entryText = text(own(entry, "text"), 1500);
      const familiar = stance === "familiar";
      const allowed = audience === "dm" ? stance === "dm" : contentStances.has(stance) || stance === "mixed" || familiar;
      const identity = token(own(entry, familiar ? "recognitionKey" : "knowledgeId"));
      if (!allowed || !entryText || !identity) { partial = true; return []; }
      const presentationKind = familiar ? "recognition" : token(own(entry, "presentationKind"));
      if (!presentationKind) partial = true;
      const admissions = [];
      const rawAdmissions = own(entry, "admissions");
      if (Array.isArray(rawAdmissions) && rawAdmissions.length <= 20) {
        const seen = new Set();
        for (const admission of rawAdmissions) {
          const actorId = token(own(admission, "actorId"));
          const actorName = text(own(admission, "actorName"), 400);
          const memberStance = own(admission, "stance");
          const source = own(admission, "source");
          if (!actorId || !actorName || seen.has(actorId) || !memberStances.has(memberStance) ||
              !["explicit", "baseline"].includes(source)) { partial = true; continue; }
          seen.add(actorId);
          const scopeId = token(own(admission, "scopeId"));
          admissions.push({ actorId, actorName, stance: memberStance, source, ...(scopeId ? { scopeId } : {}) });
        }
      } else partial = true;
      const subjectId = familiar ? null : token(own(own(entry, "subject"), "id"));
      const subjectName = familiar ? null : text(own(own(entry, "subject"), "name"), 400);
      if (!familiar && own(entry, "subject") !== undefined && (!subjectId || !subjectName)) partial = true;
      const mediaOwnerId = familiar ? null : token(own(entry, "mediaOwnerId"));
      const documentRevision = familiar ? null : token(own(entry, "documentRevision"));
      if (!familiar && !documentRevision) partial = true;
      return [{ text: entryText, stance, presentationKind: presentationKind ?? "unavailable", admissions,
        ...(familiar ? { recognitionKey: identity } : { knowledgeId: identity }),
        ...(documentRevision ? { documentRevision } : {}),
        ...(subjectId && subjectName ? { subject: { id: subjectId, name: subjectName } } : {}),
        ...(mediaOwnerId ? { mediaOwnerId } : {}) }];
    });
    const counts = new Map();
    for (const entry of projected) {
      const key = entryIdentity(entry);
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return projected.filter((entry) => {
      if (counts.get(entryIdentity(entry)) === 1) return true;
      partial = true; return false;
    });
  };
  const projectedEntries = entries(rawEntries);
  const locations = (Array.isArray(rawLocations) ? rawLocations : []).flatMap((location) => {
    const name = text(own(location, "name"), 400);
    const locationEntries = entries(own(location, "entries"));
    if (!name || locationEntries.length === 0) { partial = true; return []; }
    return [{ name, entries: locationEntries }];
  });
  return { status: value.status, audience, entries: projectedEntries, locations,
    coverage: partial ? "partial" : "complete" };
}

/** The HTTP route is generic; registration and catalog JavaScript own party and secrecy rules. */
export async function readRegisteredPartyKnowledge({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, worldId, perspective,
  onPage,
}) {
  const root = `/api/applications/${encodeURIComponent(applicationId)}/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entries = [];
  const locations = [];
  const entryIds = new Set();
  const cursors = new Set();
  let cursor = null;
  let projection = null;
  let expectedEnvelopeSourceRevision = null;
  let graphSourceRevision = null;
  let fieldCoverage = "complete";
  if (maximumPages * maximumPageBodyBytes !== maximumCumulativeBodyBytes)
    throw new Error("The shared knowledge transport bound is misconfigured.");
  for (let pageNumber = 0; pageNumber < maximumPages; pageNumber += 1) {
    const parameters = new URLSearchParams({ perspective });
    if (cursor !== null) parameters.set("input", JSON.stringify({ cursor,
      expectedSourceRevision: expectedEnvelopeSourceRevision, expectedGraphSourceRevision: graphSourceRevision }));
    const result = await readModelResponse({ fetchImpl,
      resource: new URL(`${root}/entities/${encodeURIComponent(campaignId)}/read-models/${query.id}?${parameters}`, `${origin}/`).toString(),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query, maximumBodyBytes: maximumPageBodyBytes, maximumDataBytes,
      statusPolicy: { ready: [200], forbidden: [401, 403], stale: [409, 412], unavailable: "remaining" },
      expectedSourceRevision: expectedEnvelopeSourceRevision,
      consume: (value) => {
        const sourceRevision = own(value, "sourceRevision");
        const fieldCoverage = own(value, "fieldCoverage");
        const transportCoverage = own(value, "coverage");
        const nextCursor = own(value, "nextCursor");
        const rawEntries = own(value, "entries");
        const offset = cursor === null ? 0 : Number(cursor);
        const hasNext = nextCursor !== undefined;
        const projected = projectPartyKnowledge(value, { campaignId, worldId, perspective }, {
          ignorePagingCoverage: hasNext,
        });
        if (!projected || !/^[0-9A-F]{64}$/u.test(sourceRevision ?? "") ||
            !["complete", "partial"].includes(fieldCoverage) ||
            !["complete", "partial"].includes(transportCoverage) ||
            !Array.isArray(rawEntries) || rawEntries.length > 40 ||
            (hasNext ? transportCoverage !== "partial" : transportCoverage !== fieldCoverage) || (hasNext &&
              (typeof nextCursor !== "string" || !/^[1-9][0-9]{0,3}$/u.test(nextCursor) ||
               Number(nextCursor) <= offset || Number(nextCursor) > offset + 40 ||
               Number(nextCursor) > maximumSourceEntries))) return null;
        return { ...projected, sourceRevision, fieldCoverage, transportCoverage,
          localCoverage: projected.coverage,
          nextCursor: nextCursor ?? null };
      },
    });
    if (result.status !== "ready" ||
        (projection && ["stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash"]
          .some((field) => result.evidence[field] !== projection[field])) ||
        (expectedEnvelopeSourceRevision && result.evidence.sourceRevisionFingerprint !== expectedEnvelopeSourceRevision) ||
        (graphSourceRevision && result.data.sourceRevision !== graphSourceRevision))
      throw new Error("The shared knowledge view is unavailable.");
    projection ??= result.evidence;
    expectedEnvelopeSourceRevision ??= result.evidence.sourceRevisionFingerprint;
    graphSourceRevision ??= result.data.sourceRevision;
    if (result.data.fieldCoverage === "partial" || result.data.localCoverage === "partial")
      fieldCoverage = "partial";
    const pageIds = new Set();
    if (entries.length + result.data.entries.length > maximumSourceEntries)
      throw new Error("The shared knowledge view is unavailable.");
    for (const entry of result.data.entries) {
      const id = entryIdentity(entry);
      if (pageIds.has(id) || entryIds.has(id)) throw new Error("The shared knowledge view is unavailable.");
      pageIds.add(id); entryIds.add(id); entries.push(entry);
    }
    locations.push(...result.data.locations);
    const nextCursor = result.data.nextCursor;
    if (nextCursor === null) {
      const status = entries.length || fieldCoverage === "partial" ? "ready" : "empty";
      if (!["ready", "empty"].includes(result.data.status))
        throw new Error("The shared knowledge view is unavailable.");
      return { status, audience: result.data.audience, entries, locations, coverage: fieldCoverage };
    }
    const offset = cursor === null ? 0 : Number(cursor);
    if (!['ready', 'empty'].includes(result.data.status) || result.data.transportCoverage !== "partial" ||
        Number(nextCursor) <= offset || Number(nextCursor) > offset + 40 || cursors.has(nextCursor))
      throw new Error("The shared knowledge view is unavailable.");
    // Each prefix remains fenced by the same envelope and full-graph fingerprints. A caller may
    // render this explicitly partial, authorized prefix while the owned continuation continues;
    // it never receives a synthetic complete/empty result before the terminal page.
    if (typeof onPage === "function") await onPage({
      status: "ready", audience: result.data.audience, entries: [...entries],
      locations: locations.map((location) => ({ ...location, entries: [...location.entries] })),
      coverage: "partial",
    });
    cursors.add(nextCursor);
    cursor = nextCursor;
  }
  throw new Error("The shared knowledge view is unavailable.");
}
