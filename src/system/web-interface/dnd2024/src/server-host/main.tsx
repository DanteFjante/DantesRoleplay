import { lazy, StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";

import { BootstrapShell } from "../components/BootstrapShell";
import { resolveHubSurface } from "../data/hub-availability.js";
import type { HubEnvelope, Perspective, ReadyHubEnvelope, RuleReadModel } from "../data/hub-types";
import { ViewReadClient } from "../data/view-read-client";
import { connectedCampaignToHubEnvelope } from "../server/connected-hub-envelope";
import { readGameServerContext } from "../server/game-server-context.js";
import { readInstalledContent } from "../server/effective-content";
import { readRulesReference } from "../server/rules-reference";
import { isReadyHubEnvelope } from "../state.js";
import { markBootstrapResponse, markShellReady } from "../observability/performance.js";
import {
  installDevelopmentRequestLedger,
  recordDevelopmentDiagnostic,
  withinDevelopmentInteraction,
} from "../observability/request-ledger.js";
import "../styles.css";
import "../character-page.css";

const PAGE_ASSET_BASE = "/ui/dnd2024-play/assets/";
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
  const fetchWithSignal: typeof fetch = (input, init = {}) => fetch(input, { ...init, signal });
  const sourceEnvelope = await withinDevelopmentInteraction("hub-load", () => readGameServerContext({
    serverOrigin: window.location.origin,
    fetchImpl: fetchWithSignal,
    requestedPerspective: perspective,
    requestedCampaignId: campaignId ?? null,
    localSeat: "dm",
    mediaAssetBaseUrl: PAGE_ASSET_BASE,
  })) as HubEnvelope;

  if (sourceEnvelope.status !== "connected") return sourceEnvelope;

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

async function loadEnvelope(
  perspective: Perspective,
  campaignId?: string,
): Promise<HubEnvelope> {
  return (await hubClient.load({ perspective, campaignId })).value;
}

async function loadRulesReference(): Promise<RuleReadModel[]> {
  return withinDevelopmentInteraction("rules-load", () => readRulesReference({
    serverOrigin: window.location.origin,
    applicationId: "dnd2024",
  }));
}

async function loadInstalledContent() {
  return withinDevelopmentInteraction("content-load", () =>
    readInstalledContent({ serverOrigin: window.location.origin, applicationId: "dnd2024" }));
}

async function loadReadyEnvelope(
  perspective: Perspective,
  campaignId: string,
): Promise<ReadyHubEnvelope> {
  const envelope = await loadEnvelope(perspective, campaignId);
  if (envelope.status !== "ready") {
    throw new Error(envelopeMessage(envelope));
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
markShellReady();

try {
  const initialEnvelope = await loadEnvelope("player");
  markBootstrapResponse(initialEnvelope.status);
  const surface = resolveHubSurface(initialEnvelope);
  root.render(
    <StrictMode>
      <Suspense fallback={<BootstrapShell />}>
        {surface === "table" && initialEnvelope.status === "ready" ? (
          <DndInformationHub
            initialEnvelope={initialEnvelope}
            loadEnvelope={loadReadyEnvelope}
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
