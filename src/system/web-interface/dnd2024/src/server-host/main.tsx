import { lazy, StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";

import { BootstrapShell } from "../components/BootstrapShell";
import { resolveHubSurface } from "../data/hub-availability.js";
import type { CampaignReadModel, CanonicalCharacterResult, ConnectedCampaignEnvelope, HubEnvelope, PartyMemberReadModel, Perspective, ReadyHubEnvelope, RuleReadModel, WorldFaction } from "../data/hub-types";
import { ViewReadClient, ViewReadError } from "../data/view-read-client";
import { loadInitialHub } from "../data/hub-preferences";
import { isReadyHubEnvelope } from "../state.js";
import { markBootstrapResponse } from "../observability/performance.js";
import {
  installDevelopmentRequestLedger,
  recordDevelopmentDiagnostic,
  withinDevelopmentInteraction,
} from "../observability/request-ledger.js";
import "../styles.css";
import "../character-page.css";
import "../board-draft.css";

const PAGE_ASSET_BASE = "/ui/dnd2024-play/assets/";
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
  if (isReadyHubEnvelope(value)) return true;
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
    mediaAssetBaseUrl: PAGE_ASSET_BASE,
    deferCharacterDetails: true,
    deferCampaignDetails: true,
    deferWorldDirectory: true,
    useRegisteredCampaignSummary: true,
  })) as HubEnvelope;

  if (sourceEnvelope.status !== "connected") return sourceEnvelope;
  if (signal.aborted) throw new DOMException("View replaced", "AbortError");
  const scope = characterScope(sourceEnvelope.stateSpaceId, sourceEnvelope.campaign.id, sourceEnvelope.audience.perspective);
  characterSources.delete(scope);
  characterSources.set(scope, sourceEnvelope);
  if (characterSources.size > 8) characterSources.delete(characterSources.keys().next().value!);
  characterClient.invalidate();

  const projected = connectedCampaignToHubEnvelope(
    { ...sourceEnvelope, rules: [] },
    { assetBaseUrl: PAGE_ASSET_BASE },
  );
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

const hubClient = new ViewReadClient<{
  perspective: Perspective;
  campaignId?: string;
}, HubEnvelope>({
  cacheKey: ({ perspective, campaignId }) => `${perspective}:${campaignId ?? "bound"}`,
  read: ({ perspective, campaignId }, signal) => readEnvelope(perspective, campaignId, signal),
  validate: isHubEnvelope,
});

const characterClient = new ViewReadClient<{
  source: ConnectedCampaignEnvelope; actorId: string;
}, PartyMemberReadModel>({
  cacheKey: ({ source, actorId }) => `${source.stateSpaceId}:${source.campaign.id}:${source.audience.perspective}:${actorId}`,
  validate: (value): value is PartyMemberReadModel => Boolean(value && typeof value === "object" && "sheetState" in value),
  read: async ({ source, actorId }, signal) => {
    const [{ readCanonicalCharacter }, { projectParty }] = await Promise.all([
      import("../server/game-server-context.js"), import("../server/connected-hub-envelope"),
    ]);
    if (signal.aborted) throw new DOMException("Character replaced", "AbortError");
    const member = source.party?.find((candidate) => candidate.id === actorId);
    if (!member) throw new Error("This character is not in the authorized roster.");
    const request = {
      fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => {
        const target = new URL(input instanceof Request ? input.url : input.toString(), window.location.origin);
        if (target.pathname.includes("/read-models/")) target.searchParams.set("perspective", source.audience.perspective ?? "player");
        // Media discovery currently uses the ambient host seat, not the narrower preview.
        if (source.audience.perspective === "player" && target.pathname.endsWith("/media"))
          return Promise.resolve(new Response(null, { status: 404 }));
        return fetch(target, { ...init, signal });
      },
      origin: window.location.origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
      actorId, perspective: source.audience.perspective,
    };
    const canonicalResult = await readCanonicalCharacter(request) as CanonicalCharacterResult;
    return projectParty({ ...source, party: [{ ...member, detailsDeferred: false, canonicalResult,
      ...(canonicalResult.status === "ready" ? { canonical: canonicalResult.data } : {}),
    }] })[0];
  },
});

async function loadCharacter(envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) {
  const source = characterSources.get(characterScope(envelope.stateSpaceId,
    envelope.contextSelection?.selectedCampaignId ?? "", envelope.audience.perspective));
  if (!source || source.campaign.id !== envelope.contextSelection?.selectedCampaignId ||
      source.audience.perspective !== envelope.audience.perspective || signal.aborted)
    throw new Error("Refresh this view before opening a character.");
  const request = { source, actorId };
  const cached = characterClient.peek(request);
  if (cached && cached.value.sheetState.status !== "error" && cached.value.sheetState.status !== "forbidden") return cached.value;
  const cancel = () => characterClient.cancel();
  signal.addEventListener("abort", cancel, { once: true });
  try { return (await characterClient.load(request)).value; }
  finally { signal.removeEventListener("abort", cancel); }
}

async function loadFactionPage(
  envelope: ReadyHubEnvelope,
  cursor: string | null,
  signal: AbortSignal,
): Promise<{ factions: WorldFaction[]; totalCount: number; complete: boolean; nextCursor: string | null; sourceRevisionFingerprint: string | null }> {
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
  const projected = connectedCampaignToHubEnvelope({ ...source,
    worldDirectory: { people: [], factions: page.factions, holdings: [] }, rules: [],
  }, { assetBaseUrl: PAGE_ASSET_BASE });
  const ids = new Set(page.factions.map((faction: { id: string }) => faction.id));
  return {
    factions: projected.world.factions.filter((faction) => ids.has(faction.id)),
    totalCount: page.totalCount,
    complete: page.complete,
    nextCursor: page.nextCursor,
    sourceRevisionFingerprint: page.sourceRevisionFingerprint ?? null,
  };
}

async function loadCampaignDetails(
  envelope: ReadyHubEnvelope,
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
  return connectedCampaignToHubEnvelope({ ...source, campaign: { ...source.campaign, ...details }, rules: [] },
    { assetBaseUrl: PAGE_ASSET_BASE }).campaign;
}

async function loadEnvelope(
  perspective: Perspective,
  campaignId?: string,
): Promise<HubEnvelope> {
  return (await hubClient.load({ perspective, campaignId })).value;
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
  const explicit = preferCached ? hubClient.peek(request) : null;
  const bound = preferCached && explicit === null
    ? hubClient.peek({ perspective })
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
  if (typeof EventSource !== "undefined") {
    const changes = new EventSource("/api/changes?page=dnd2024-play");
    let connected = false;
    const invalidate = () => {
      hubClient.invalidate();
      characterClient.invalidate();
      window.dispatchEvent(new Event("dnd2024-view-invalidated"));
    };
    changes.addEventListener("invalidate", (event) => {
      try {
        const firstConnection = !connected && JSON.parse(event.data).reason === "connected";
        connected = true;
        if (firstConnection) return;
      } catch { /* An unreadable event invalidates rather than reusing private data. */ }
      invalidate();
    });
    changes.addEventListener("error", invalidate);
    window.addEventListener("pagehide", () => { changes.close(); invalidate(); }, { once: true });
  }
  markBootstrapResponse(initialEnvelope.status);
  const surface = resolveHubSurface(initialEnvelope);
  root.render(
    <StrictMode>
      <Suspense fallback={<BootstrapShell />}>
        {surface === "table" && initialEnvelope.status === "ready" ? (
          <DndInformationHub
            initialEnvelope={initialEnvelope}
            loadEnvelope={loadReadyEnvelope}
            loadCharacter={loadCharacter}
            loadFactionPage={loadFactionPage}
            loadCampaignDetails={loadCampaignDetails}
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
