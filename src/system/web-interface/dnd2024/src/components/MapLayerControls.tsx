import type { MapFeature, MapLayer } from "../data/hub-types";
import { Icon } from "./Icon";

export function MapLayerControls({
  layers,
  features,
  visibleLayerIds,
  onToggle,
}: {
  layers: MapLayer[];
  features: MapFeature[];
  visibleLayerIds: Set<string>;
  onToggle: (layerId: string) => void;
}) {
  if (layers.length < 2) return null;

  return (
    <section className="map-layer-controls" aria-label="Map layers">
      <span className="map-layer-controls__label">
        <Icon name="Layers3" size={14} />
        Layers
      </span>
      <div className="map-layer-controls__options">
        {layers.map((layer) => {
          const count = features.filter((feature) => feature.layerId === layer.id).length;
          return (
            <button
              aria-pressed={visibleLayerIds.has(layer.id)}
              key={layer.id}
              onClick={() => onToggle(layer.id)}
              type="button"
            >
              <span>{layer.label}</span>
              <small>{count}</small>
            </button>
          );
        })}
      </div>
    </section>
  );
}
