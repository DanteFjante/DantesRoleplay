import type { HubContextSelection, Perspective } from "../data/hub-types";
import { PerspectiveSwitch } from "./PerspectiveSwitch";
import { WorldCampaignSelector } from "./WorldCampaignSelector";

export function TopBar({
  perspective,
  allowedPerspectives,
  busy,
  contextSelection,
  onCampaignChange,
  onPerspectiveChange,
}: {
  perspective: Perspective;
  allowedPerspectives: Perspective[];
  busy: boolean;
  contextSelection: HubContextSelection;
  onCampaignChange: (campaignId: string) => void;
  onPerspectiveChange: (perspective: Perspective) => void;
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
      />
      <PerspectiveSwitch
        allowedPerspectives={allowedPerspectives}
        busy={busy}
        perspective={perspective}
        onChange={onPerspectiveChange}
      />
    </header>
  );
}
