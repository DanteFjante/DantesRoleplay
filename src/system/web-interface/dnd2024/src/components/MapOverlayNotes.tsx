import type { CampaignMapOverlay } from "../data/hub-types";
import { Icon } from "./Icon";

const KIND_LABELS: Record<string, string> = { note: "Note", reveal: "Revealed" };

export function MapOverlayNotes({
  campaignTitle,
  overlays,
  onSelectFeature,
}: {
  campaignTitle: string;
  overlays: CampaignMapOverlay[];
  onSelectFeature: (featureId: string) => void;
}) {
  return (
    <section className="panel map-overlay-notes" aria-label={`${campaignTitle} notes on this map`}>
      <span className="eyebrow">From {campaignTitle}</span>
      {overlays.length === 0 ? (
        <p className="map-overlay-notes__empty">Your campaign has nothing recorded on this map.</p>
      ) : (
        <ul>
          {overlays.map((overlay) => (
            <li key={overlay.id} data-kind={overlay.kind}>
              <p className="map-overlay-notes__head">
                <span aria-hidden="true">
                  <Icon name={overlay.kind === "reveal" ? "Eye" : "ScrollText"} size={15} />
                </span>
                <strong>{overlay.label}</strong>
                <small>{KIND_LABELS[overlay.kind] ?? overlay.kind} · {overlay.recordedOn}</small>
              </p>
              <p>{overlay.detail}</p>
              {overlay.featureId === null ? null : (
                <button
                  className="text-action"
                  onClick={() => onSelectFeature(overlay.featureId as string)}
                  type="button"
                >
                  Show on the map <Icon name="ArrowRight" size={15} />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      <p className="map-overlay-notes__boundary">
        Campaign notes annotate this map. They never change its places or their positions.
      </p>
    </section>
  );
}
