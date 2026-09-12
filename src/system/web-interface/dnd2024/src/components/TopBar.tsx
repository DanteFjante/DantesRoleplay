import type { DeferredViewState, HubContextSelection, Perspective } from "../data/hub-types";
import { PerspectiveSwitch } from "./PerspectiveSwitch";
import { WorldCampaignSelector } from "./WorldCampaignSelector";
import { SystemControls } from "./SystemControls";

export function TopBar({
  perspective,
  allowedPerspectives,
  busy,
  contextSelection,
  onCampaignChange,
  onPerspectiveChange,
  onOpenContext,
  contextState,
  contextError,
  sharedAccess = false,
}: {
  perspective: Perspective;
  allowedPerspectives: Perspective[];
  busy: boolean;
  contextSelection: HubContextSelection;
  onCampaignChange: (campaignId: string) => void;
  onPerspectiveChange: (perspective: Perspective) => void;
  onOpenContext?: () => void;
  contextState?: DeferredViewState;
  contextError?: string;
  sharedAccess?: boolean;
}) {
  return (
    <header className="top-bar">
      <div className="brand-lockup" aria-label="Dante's Roleplay">
        <span className="brand-lockup__die" aria-hidden="true">20</span>
        <span className="brand-lockup__copy">
          <strong>Dante&apos;s Roleplay</strong>
          <small>D&amp;D 2024 table</small>
        </span>
      </div>
      <WorldCampaignSelector
        busy={busy}
        onCampaignChange={onCampaignChange}
        selection={contextSelection}
        onOpen={onOpenContext}
        loadState={contextState}
        error={contextError}
      />
      {allowedPerspectives.length > 1 ? <PerspectiveSwitch
        allowedPerspectives={allowedPerspectives}
        busy={busy}
        perspective={perspective}
        onChange={onPerspectiveChange}
      /> : <span className="perspective-switch__label">
        {sharedAccess && perspective === "dm" ? "Shared table" : "Player view"}
      </span>}
      <SystemControls playerOnly={!allowedPerspectives.includes("dm")} />
    </header>
  );
}
