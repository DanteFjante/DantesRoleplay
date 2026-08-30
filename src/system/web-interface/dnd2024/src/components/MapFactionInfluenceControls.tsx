import type { MapFactionInfluence } from "../data/hub-types";
import { Icon } from "./Icon";

export function MapFactionInfluenceControls({
  influences,
  selectedFactionId,
  onSelect,
}: {
  influences: MapFactionInfluence[];
  selectedFactionId: string;
  onSelect: (factionId: string) => void;
}) {
  if (influences.length === 0) return null;

  return (
    <section className="map-faction-controls" aria-label="Faction influence overlay">
      <span className="map-faction-controls__label">
        <Icon name="Shield" size={14} />
        Faction influence
      </span>
      <div>
        <button
          aria-pressed={selectedFactionId === ""}
          onClick={() => onSelect("")}
          type="button"
        >
          None
        </button>
        {influences.map((influence) => (
          <button
            aria-pressed={selectedFactionId === influence.factionId}
            key={influence.factionId}
            onClick={() => onSelect(influence.factionId)}
            title={`${influence.influence} influence; ${influence.featureIds.length} mapped ${
              influence.featureIds.length === 1 ? "place" : "places"
            } in this scope`}
            type="button"
          >
            {influence.name}
            <small>{influence.featureIds.length}</small>
          </button>
        ))}
      </div>
      <p>Highlights recorded presence at exact locations, not borders or exclusive control.</p>
    </section>
  );
}
