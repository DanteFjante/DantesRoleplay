import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { DndInformationHub } from "../components/DndInformationHub";
import { HubUnavailable } from "../components/HubUnavailable";
import type { HubEnvelope, Perspective, ReadyHubEnvelope } from "../data/hub-types";
import { connectedCampaignToHubEnvelope } from "../server/connected-hub-envelope";
import { readGameServerContext } from "../server/game-server-context.js";
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
  }) as HubEnvelope;

  if (sourceEnvelope.status !== "connected") return sourceEnvelope;

  const rules = await readRulesReference({
    serverOrigin: window.location.origin,
    applicationId: sourceEnvelope.applicationId,
  });
  return connectedCampaignToHubEnvelope(
    { ...sourceEnvelope, rules },
    { assetBaseUrl: PAGE_ASSET_BASE },
  );
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
  root.render(
    <StrictMode>
      {initialEnvelope.status === "ready" ? (
        <DndInformationHub
          initialEnvelope={initialEnvelope}
          loadEnvelope={loadReadyEnvelope}
        />
      ) : (
        <HubUnavailable message={initialEnvelope.message} />
      )}
    </StrictMode>,
  );
} catch {
  root.render(
    <StrictMode>
      <HubUnavailable message="The live D&D server could not prepare the table view." />
    </StrictMode>,
  );
}
