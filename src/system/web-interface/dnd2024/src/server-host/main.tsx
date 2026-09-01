import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { DndInformationHub } from "../components/DndInformationHub";
import { RulesOnlyHub } from "../components/RulesOnlyHub";
import { resolveHubSurface } from "../data/hub-availability.js";
import type { HubEnvelope, Perspective, ReadyHubEnvelope, RuleReadModel } from "../data/hub-types";
import { connectedCampaignToHubEnvelope } from "../server/connected-hub-envelope";
import { readGameServerContext } from "../server/game-server-context.js";
import { readInstalledContent } from "../server/effective-content";
import { readRulesReference } from "../server/rules-reference";
import "../styles.css";

const PAGE_ASSET_BASE = "/ui/dnd2024-play/assets/";

async function loadEnvelope(
  perspective: Perspective,
  campaignId?: string,
): Promise<HubEnvelope> {
  const sourceEnvelope = await readGameServerContext({
    serverOrigin: window.location.origin,
    requestedPerspective: perspective,
    requestedCampaignId: campaignId,
    localSeat: "dm",
    mediaAssetBaseUrl: PAGE_ASSET_BASE,
  }) as HubEnvelope;

  if (sourceEnvelope.status !== "connected") return sourceEnvelope;

  return connectedCampaignToHubEnvelope(
    { ...sourceEnvelope, rules: [] },
    { assetBaseUrl: PAGE_ASSET_BASE },
  );
}

async function loadRulesReference(): Promise<RuleReadModel[]> {
  return readRulesReference({
    serverOrigin: window.location.origin,
    applicationId: "dnd2024",
  });
}

async function loadInstalledContent() {
  return readInstalledContent({ serverOrigin: window.location.origin, applicationId: "dnd2024" });
}

async function loadReadyEnvelope(
  perspective: Perspective,
  campaignId: string,
): Promise<ReadyHubEnvelope> {
  const envelope = await loadEnvelope(perspective, campaignId);
  if (envelope.status !== "ready") {
    throw new Error(envelope.message);
  }
  return envelope;
}

const rootElement = document.querySelector<HTMLElement>("#root");
if (!rootElement) throw new Error("The React mount is unavailable.");
const root = createRoot(rootElement);

try {
  const initialEnvelope = await loadEnvelope("player");
  const surface = resolveHubSurface(initialEnvelope);
  root.render(
    <StrictMode>
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
          message={initialEnvelope.message}
        />
      )}
    </StrictMode>,
  );
} catch {
  root.render(
    <StrictMode>
      <RulesOnlyHub
        loadRules={loadRulesReference}
        loadContent={loadInstalledContent}
        message="The private campaign view could not be prepared."
      />
    </StrictMode>,
  );
}
