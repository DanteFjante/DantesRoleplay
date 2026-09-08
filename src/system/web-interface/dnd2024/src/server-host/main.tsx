import { lazy, StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";

import { BootstrapShell } from "../components/BootstrapShell";
import {
  CharacterResourceOwner,
  CurrentViewResourceOwner,
  TableResourceOwner,
  WorldResourceOwner,
  type CampaignContextObjectRequest,
  type CampaignContextUpdate,
  type CampaignDetailsObjectRequest,
  type CharacterResourceRequest,
  type CurrentViewRequest,
  type CurrentViewUpdate,
  type FactionObjectRequest,
  type WorldScopeRequest,
  type WorldScopeUpdate,
  type WorldInformationRequest,
  type WorldInformationUpdate,
} from "../data/object-resources";
import { resolveHubSurface } from "../data/hub-availability.js";
import type { CampaignReadModel, CanonicalCharacterResult, CharacterSheetResult, ConnectedCampaignEnvelope, DeferredHubSection, DeferredHubUpdate, HubEnvelope, InventoryContainerResult, Perspective, ReadyHubEnvelope, RuleReadModel } from "../data/hub-types";
import { ViewReadError } from "../data/view-read-client";
import { loadInitialHub } from "../data/hub-preferences";
import { objectConsumers, subscribeScopedChanges } from "../data/scoped-change-stream";
import { isReadyHubEnvelope } from "../state.js";
import { markBootstrapResponse } from "../observability/performance.js";
import type { CampaignPremiseWriteRequest, CampaignPremiseWriteResult } from "../server/campaign-premise-write";
import {
  installDevelopmentRequestLedger,
  recordDevelopmentDiagnostic,
  withinDevelopmentInteraction,
} from "../observability/request-ledger.js";
import "../styles.css";

const characterSources = new Map<string, ConnectedCampaignEnvelope>();
const characterScope = (state: string, campaign: string, perspective?: Perspective) => `${state}:${campaign}:${perspective ?? "player"}`;
const DndInformationHub = lazy(() => import("../components/DndInformationHub")
  .then((module) => ({ default: module.DndInformationHub })));
const RulesOnlyHub = lazy(() => import("../components/RulesOnlyHub")
  .then((module) => ({ default: module.RulesOnlyHub })));

if (process.env.NODE_ENV !== "production") installDevelopmentRequestLedger();

function envelopeMessage(envelope: HubEnvelope): string {
  return "message" in envelope
    ? envelope.message
    : "The private campaign view is not ready yet.";
}

function isHubEnvelope(value: unknown): value is HubEnvelope {
  if (isReadyHubEnvelope(value)) {
    const projection = (value as ReadyHubEnvelope).objectQueries?.campaignSummary;
    return Boolean(projection && projection.qualifiedQueryId === "dnd2024.query.campaign-summary" &&
      [projection.stateSpaceFingerprint, projection.resolutionFingerprint, projection.outputSchemaHash,
        projection.resultFingerprint, projection.sourceRevisionFingerprint]
        .every((fingerprint) => /^[0-9A-F]{64}$/iu.test(fingerprint)));
  }
  if (!value || typeof value !== "object") return false;
  const envelope = value as Record<string, unknown>;
  if (envelope.version !== 1 || typeof envelope.status !== "string") return false;
  if (envelope.status === "denied" || envelope.status === "unavailable") {
    return typeof envelope.message === "string";
  }
  if (envelope.status === "character-creation-required") {
    return ["applicationId", "stateSpaceId", "campaignId", "characterId", "message"]
      .every((key) => typeof envelope[key] === "string");
  }
  return false;
}

async function readEnvelope(
  perspective: Perspective,
  campaignId: string | undefined,
  signal: AbortSignal,
): Promise<HubEnvelope> {
  const [{ readGameServerContext }, { connectedCampaignToHubEnvelope }] = await Promise.all([
    import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
  ]);
  if (signal.aborted) throw new DOMException("View replaced", "AbortError");
  const fetchWithSignal: typeof fetch = (input, init = {}) => fetch(input, { ...init, signal });
  const sourceEnvelope = await withinDevelopmentInteraction("hub-load", () => readGameServerContext({
    serverOrigin: window.location.origin,
    fetchImpl: fetchWithSignal,
    requestedPerspective: perspective,
    requestedCampaignId: campaignId ?? null,
  })) as HubEnvelope | ConnectedCampaignEnvelope;

  if (sourceEnvelope.status !== "connected") return sourceEnvelope;
  if (signal.aborted) throw new DOMException("View replaced", "AbortError");
  const scope = characterScope(sourceEnvelope.stateSpaceId, sourceEnvelope.campaign.id, sourceEnvelope.audience.perspective);
  characterSources.delete(scope);
  characterSources.set(scope, sourceEnvelope);
  if (characterSources.size > 8) characterSources.delete(characterSources.keys().next().value!);
  const projected = connectedCampaignToHubEnvelope(
    { ...sourceEnvelope, rules: [] },
  );
  characterResources.replaceScope(projected);
  worldResources.replaceScope(projected);
  currentViewResources.replaceScope(projected);
  recordDevelopmentDiagnostic("party-read", {
    applicationId: projected.applicationId,
    stateSpaceId: projected.stateSpaceId,
    campaignId: projected.contextSelection?.selectedCampaignId ?? projected.revision,
    audience: {
      seat: projected.audience.seat,
      perspective: projected.audience.perspective,
    },
    partyDiscovery: projected.party.length === 0 ? "empty" : "ready",
    partySize: projected.party.length,
    sourceRevision: projected.revision,
    members: projected.party.map((member) => ({
      actorId: member.id,
      readModelStatus: member.sheetState.status,
      sourceRevisionFingerprint: member.characterSheet?.projection?.sourceRevisionFingerprint ?? null,
      sections: {
        sheet: member.sheetState.status,
        inventory: member.inventoryState.status,
      },
      diagnosticId: "diagnosticId" in member.sheetState ? member.sheetState.diagnosticId : null,
    })),
  });
  return projected;
}

const tableResources = new TableResourceOwner({
  readCampaign: ({ perspective, campaignId }, signal) => readEnvelope(perspective, campaignId, signal),
  readFactionPage: readFactionObjectPage,
  readCampaignDetails: readCampaignDetailsObject,
  readCampaignContext: readCampaignContextObject,
  validateCampaign: isHubEnvelope,
});

const characterResources = new CharacterResourceOwner({
  readSheet: readCharacterSheetResource,
  readDetails: readCharacterDetailsResource,
  readInventory: readCharacterInventoryResource,
});

const worldResources = new WorldResourceOwner({
  readScope: readWorldScopeObject,
  readInformation: readWorldInformationObject,
});

const currentViewResources = new CurrentViewResourceOwner({ readCurrent: readCurrentViewObject });

function authorizedCharacter({ envelope, actorId }: CharacterResourceRequest) {
  const member = envelope.party.find((candidate) => candidate.id === actorId);
  if (!member) throw new Error("This character is not in the authorized roster.");
  return member;
}

function characterReadRequest({ envelope, actorId }: CharacterResourceRequest, signal: AbortSignal) {
  return {
    fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, { ...init, signal }),
    origin: window.location.origin,
    applicationId: envelope.applicationId,
    stateSpaceId: envelope.stateSpaceId,
    actorId,
    perspective: envelope.audience.perspective,
  };
}

async function readCharacterSheetResource(request: CharacterResourceRequest, signal: AbortSignal) {
  const member = authorizedCharacter(request);
  const [{ readCanonicalCharacterSheet }, { projectCharacterSheet }] = await Promise.all([
    import("../server/game-server-context.js"), import("../features/character/project-character"),
  ]);
  if (signal.aborted) throw new DOMException("Character replaced", "AbortError");
  return projectCharacterSheet(member,
    await readCanonicalCharacterSheet(characterReadRequest(request, signal)) as CharacterSheetResult);
}

async function readCharacterDetailsResource(request: CharacterResourceRequest, signal: AbortSignal) {
  const member = authorizedCharacter(request);
  const [{ readCanonicalCharacter }, { projectCharacterDetails }] = await Promise.all([
    import("../server/game-server-context.js"), import("../features/character/project-character"),
  ]);
  if (signal.aborted) throw new DOMException("Character replaced", "AbortError");
  return projectCharacterDetails(member,
    await readCanonicalCharacter(characterReadRequest(request, signal)) as CanonicalCharacterResult);
}

async function readCharacterInventoryResource(request: CharacterResourceRequest, signal: AbortSignal) {
  authorizedCharacter(request);
  const { readCanonicalInventory } = await import("../server/game-server-context.js");
  if (signal.aborted) throw new DOMException("Inventory replaced", "AbortError");
  return await readCanonicalInventory(characterReadRequest(request, signal)) as InventoryContainerResult;
}

async function loadCharacterSheet(envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) {
  return characterResources.loadSheet({ envelope, actorId }, signal);
}

async function loadCharacterDetails(envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) {
  return characterResources.loadDetails({ envelope, actorId }, signal);
}

async function loadCharacterInventory(envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) {
  return characterResources.loadInventory({ envelope, actorId }, signal);
}

async function readFactionObjectPage(
  { envelope, cursor }: FactionObjectRequest,
  signal: AbortSignal,
) {
  const source = characterSources.get(characterScope(envelope.stateSpaceId,
    envelope.contextSelection?.selectedCampaignId ?? "", envelope.audience.perspective));
  if (!source || source.audience.seat !== "dm" || source.audience.perspective !== "dm" || signal.aborted)
    throw new Error("The faction directory is unavailable to this audience.");
  const [{ readRegisteredFactionDirectoryPage }, { connectedCampaignToHubEnvelope }] = await Promise.all([
    import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
  ]);
  const page = await readRegisteredFactionDirectoryPage({
    fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, { ...init, signal }),
    origin: window.location.origin,
    applicationId: source.applicationId,
    stateSpaceId: source.stateSpaceId,
    worldId: envelope.contextSelection?.selectedWorldId ?? "",
    cursor,
  });
  if (!page) throw new Error("The faction directory could not be read.");
  if (signal.aborted) throw new DOMException("View replaced", "AbortError");
  const key = characterScope(source.stateSpaceId, source.campaign.id, source.audience.perspective);
  const latest = characterSources.get(key);
  if (!latest) throw new DOMException("View replaced", "AbortError");
  const factions = cursor === null ? page.factions : [
    ...(latest.worldDirectory?.factions ?? []),
    ...page.factions.filter((item: { id: string }) =>
      !latest.worldDirectory?.factions.some((previous) => previous.id === item.id)),
  ];
  characterSources.set(key, {
    ...latest, worldDirectory: { people: latest.worldDirectory?.people ?? [], factions,
      holdings: latest.worldDirectory?.holdings ?? [] },
  });
  const projected = connectedCampaignToHubEnvelope({ ...source,
    worldDirectory: { people: [], factions: page.factions, holdings: [] }, rules: [],
  });
  const ids = new Set(page.factions.map((faction: { id: string }) => faction.id));
  return {
    factions: projected.world.factions.filter((faction) => ids.has(faction.id)),
    totalCount: page.totalCount,
    complete: page.complete,
    nextCursor: page.nextCursor,
    sourceRevisionFingerprint: page.sourceRevisionFingerprint ?? null,
    projection: page.projection,
  };
}

async function loadFactionPage(envelope: ReadyHubEnvelope, cursor: string | null, signal: AbortSignal) {
  return tableResources.loadFactionPage({ envelope, cursor }, signal);
}

async function readCampaignDetailsObject(
  { envelope }: CampaignDetailsObjectRequest,
  signal: AbortSignal,
): Promise<CampaignReadModel> {
  const source = characterSources.get(characterScope(envelope.stateSpaceId,
    envelope.contextSelection?.selectedCampaignId ?? "", envelope.audience.perspective));
  if (!source || signal.aborted) throw new Error("The campaign details are unavailable.");
  const [{ readDeferredCampaignDetails }, { connectedCampaignToHubEnvelope }] = await Promise.all([
    import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
  ]);
  const details = await readDeferredCampaignDetails({
    fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, { ...init, signal }),
    origin: window.location.origin,
    source,
  });
  if (details?.incomplete) throw new Error("The campaign details could not be read completely.");
  if (signal.aborted) throw new DOMException("View replaced", "AbortError");
  const key = characterScope(source.stateSpaceId, source.campaign.id, source.audience.perspective);
  const latest = characterSources.get(key);
  if (!latest) throw new DOMException("View replaced", "AbortError");
  characterSources.set(key, { ...latest, campaign: { ...latest.campaign, ...details } });
  return connectedCampaignToHubEnvelope({
    ...latest,
    campaign: { ...latest.campaign, ...details },
    rules: [],
  }).campaign;
}

async function loadCampaignDetails(envelope: ReadyHubEnvelope, signal: AbortSignal) {
  return tableResources.loadCampaignDetails({ envelope }, signal);
}

async function loadEnvelope(
  perspective: Perspective,
  campaignId?: string,
): Promise<HubEnvelope> {
  return tableResources.loadCampaign({ perspective, campaignId });
}

async function readDeferredSectionObject(
  { envelope }: CampaignContextObjectRequest,
  section: DeferredHubSection,
  signal: AbortSignal,
): Promise<DeferredHubUpdate> {
  const key = characterScope(envelope.stateSpaceId,
    envelope.contextSelection?.selectedCampaignId ?? "", envelope.audience.perspective);
  const source = characterSources.get(key);
  if (!source || signal.aborted) throw new Error("Refresh the authorized view before continuing.");
  const [{ readDeferredHubSection }, { connectedCampaignToDeferredHubUpdate }] = await Promise.all([
    import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
  ]);
  const patch = await readDeferredHubSection({
    fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, { ...init, signal }),
    origin: window.location.origin, source, section,
  });
  const latest = characterSources.get(key);
  if (signal.aborted || !latest)
    throw new DOMException("View replaced", "AbortError");
  // Independent context discovery may finish beside a section read. Merge their disjoint
  // patches; the hub aborts both owners before replacing the authorized bootstrap.
  const updated = { ...latest, ...patch };
  characterSources.set(key, updated);
  return connectedCampaignToDeferredHubUpdate({ ...updated, rules: [] }, section);
}

async function readWorldScopeObject(
  { envelope, scopeId }: WorldScopeRequest,
  signal: AbortSignal,
): Promise<WorldScopeUpdate> {
  const key = characterScope(envelope.stateSpaceId,
    envelope.contextSelection?.selectedCampaignId ?? "", envelope.audience.perspective);
  const source = characterSources.get(key);
  if (!source || signal.aborted) throw new Error("Refresh the authorized World view before continuing.");
  const [{ readWorldLocationScopePatch }, { connectedCampaignToDeferredHubUpdate }] = await Promise.all([
    import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
  ]);
  const patch = await readWorldLocationScopePatch({
    fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, { ...init, signal }),
    origin: window.location.origin, source, scopeId,
  });
  const latest = characterSources.get(key);
  if (signal.aborted || !latest) throw new DOMException("World scope replaced", "AbortError");
  const updated = { ...latest, ...patch };
  characterSources.set(key, updated);
  const projected = connectedCampaignToDeferredHubUpdate({ ...updated, rules: [] }, "locations");
  if (projected.section !== "locations") throw new Error("The World scope response is incompatible.");
  return projected;
}

async function loadWorldScope(envelope: ReadyHubEnvelope, scopeId: string, signal: AbortSignal) {
  return worldResources.loadScope({ envelope, scopeId }, signal);
}

async function readWorldInformationObject(
  request: WorldInformationRequest,
  signal: AbortSignal,
): Promise<WorldInformationUpdate> {
  const update = await readDeferredSectionObject(request, request.section, signal);
  if (update.section !== request.section)
    throw new Error("The World information response is incompatible.");
  return update as WorldInformationUpdate;
}

async function readCurrentViewObject(
  request: CurrentViewRequest,
  signal: AbortSignal,
): Promise<CurrentViewUpdate> {
  const update = await readDeferredSectionObject(request, "current", signal);
  if (update.section !== "current") throw new Error("The Current View response is incompatible.");
  return update;
}

async function readCampaignContextObject(
  request: CampaignContextObjectRequest,
  signal: AbortSignal,
): Promise<CampaignContextUpdate> {
  const update = await readDeferredSectionObject(request, "context", signal);
  if (update.section !== "context") throw new Error("The campaign context response is incompatible.");
  return update;
}

async function loadDeferredSection(
  envelope: ReadyHubEnvelope,
  section: DeferredHubSection,
  signal: AbortSignal,
): Promise<DeferredHubUpdate> {
  return section === "context"
    ? tableResources.loadCampaignContext({ envelope }, signal)
    : section === "locations"
      ? loadWorldScope(envelope, envelope.contextSelection?.selectedWorldId ?? envelope.world.id, signal)
      : ["people", "lore", "history"].includes(section)
        ? worldResources.loadInformation({ envelope, section: section as WorldInformationRequest["section"] }, signal)
        : section === "current"
          ? currentViewResources.loadCurrent({ envelope }, signal)
          : readDeferredSectionObject({ envelope }, section, signal);
}

async function loadRulesReference(): Promise<RuleReadModel[]> {
  const { readRulesReference } = await import("../server/rules-reference");
  return withinDevelopmentInteraction("rules-load", () => readRulesReference({
    serverOrigin: window.location.origin,
    applicationId: "dnd2024",
  }));
}

async function loadInstalledContent() {
  const { readInstalledContent } = await import("../server/effective-content");
  return withinDevelopmentInteraction("content-load", () =>
    readInstalledContent({ serverOrigin: window.location.origin, applicationId: "dnd2024" }));
}

async function loadReadyEnvelope(
  perspective: Perspective,
  campaignId: string,
  preferCached = false,
): Promise<ReadyHubEnvelope> {
  const request = { perspective, campaignId };
  const explicit = preferCached ? tableResources.peekCampaign(request) : null;
  const bound = preferCached && explicit === null
    ? tableResources.peekCampaign({ perspective })
    : null;
  const cached = explicit ?? (bound?.value.status === "ready" &&
    bound.value.contextSelection?.selectedCampaignId === campaignId
    ? bound
    : null);
  const envelope = cached?.value ?? await loadEnvelope(perspective, campaignId);
  if (envelope.status !== "ready") {
    throw new ViewReadError("transport", envelopeMessage(envelope));
  }
  return envelope;
}

const rootElement = document.querySelector<HTMLElement>("#root");
function subscribeChanges(envelope: ReadyHubEnvelope) {
  if (typeof EventSource === "undefined") return () => {};
  const invalidate = () => {
    tableResources.invalidateAll();
    characterResources.invalidateAll();
    worldResources.invalidateAll();
    currentViewResources.invalidateAll();
    window.dispatchEvent(new Event("dnd2024-view-invalidated"));
  };
  return subscribeScopedChanges(envelope, {
    invalidate,
    changed: (notice) => {
      const consumers = objectConsumers(notice.object.qualifiedId);
      if (!consumers.known) { invalidate(); return; }
      tableResources.invalidateObject(notice.object.qualifiedId);
      if (consumers.character) characterResources.invalidateObject(notice.object.qualifiedId);
      currentViewResources.invalidateObject(notice.object.qualifiedId);
      window.dispatchEvent(new CustomEvent("dnd2024-object-changed", { detail: notice }));
    },
  });
}

async function saveCampaignPremise(
  request: CampaignPremiseWriteRequest,
  signal?: AbortSignal,
): Promise<CampaignPremiseWriteResult> {
  const { writeCampaignPremise } = await import("../server/campaign-premise-write");
  return writeCampaignPremise(request, signal);
}
if (!rootElement) throw new Error("The React mount is unavailable.");
const root = createRoot(rootElement);
root.render(
  <StrictMode>
    <BootstrapShell />
  </StrictMode>,
);

try {
  const initialEnvelope = await loadInitialHub(loadEnvelope, {
    getItem: (key) => window.localStorage.getItem(key),
  });
  markBootstrapResponse(initialEnvelope.status);
  const surface = resolveHubSurface(initialEnvelope);
  root.render(
    <StrictMode>
      <Suspense fallback={<BootstrapShell />}>
        {surface === "table" && initialEnvelope.status === "ready" ? (
          <DndInformationHub
            initialEnvelope={initialEnvelope}
            subscribeChanges={subscribeChanges}
            loadEnvelope={loadReadyEnvelope}
            loadCharacterSheet={loadCharacterSheet}
            loadCharacterDetails={loadCharacterDetails}
            loadCharacterInventory={loadCharacterInventory}
            loadFactionPage={loadFactionPage}
            loadCampaignDetails={loadCampaignDetails}
            loadDeferredSection={loadDeferredSection}
            loadWorldScope={loadWorldScope}
            writeCampaignPremise={saveCampaignPremise}
            loadRules={loadRulesReference}
            loadContent={loadInstalledContent}
          />
        ) : (
          <RulesOnlyHub
            loadRules={loadRulesReference}
            loadContent={loadInstalledContent}
            message={envelopeMessage(initialEnvelope)}
          />
        )}
      </Suspense>
    </StrictMode>,
  );
} catch {
  markBootstrapResponse("error");
  root.render(
    <StrictMode>
      <Suspense fallback={<BootstrapShell />}>
        <RulesOnlyHub
          loadRules={loadRulesReference}
          loadContent={loadInstalledContent}
          message="The private campaign view could not be prepared."
        />
      </Suspense>
    </StrictMode>,
  );
}
