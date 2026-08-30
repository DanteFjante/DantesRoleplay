import { Icon } from "./Icon";

export type MapViewMode = "map" | "list";

export function MapViewModeToggle({
  mode,
  onChange,
}: {
  mode: MapViewMode;
  onChange: (mode: MapViewMode) => void;
}) {
  return (
    <div className="map-view-mode" aria-label="Map presentation" role="group">
      <button aria-pressed={mode === "map"} onClick={() => onChange("map")} type="button">
        <Icon name="Map" size={14} /> Illustrated
      </button>
      <button aria-pressed={mode === "list"} onClick={() => onChange("list")} type="button">
        <Icon name="ScrollText" size={14} /> List
      </button>
    </div>
  );
}
