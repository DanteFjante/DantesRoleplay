import type { MapFeature, MapLayer } from "../data/hub-types";
import { Icon } from "./Icon";

export function MapLegend({
  layers,
  features,
  visibleLayerIds,
  currentLocationId,
  scopeLinkFeatureIds,
  annotatedFeatureIds,
  influencedFeatureIds,
}: {
  layers: MapLayer[];
  features: MapFeature[];
  visibleLayerIds: Set<string>;
  currentLocationId: string;
  scopeLinkFeatureIds: Map<string, string>;
  annotatedFeatureIds: Set<string>;
  influencedFeatureIds: Set<string>;
}) {
  const visibleLayers = layers.filter((layer) => visibleLayerIds.has(layer.id));
  const visibleFeatures = features.filter((feature) => visibleLayerIds.has(feature.layerId));
  const states = [
    visibleFeatures.some((feature) => feature.locationId === currentLocationId)
      ? { id: "current", label: "Current place", icon: "LocateFixed" as const }
      : null,
    visibleFeatures.some((feature) => scopeLinkFeatureIds.has(feature.id))
      ? { id: "scope", label: "Closer map", icon: "Landmark" as const }
      : null,
    visibleFeatures.some((feature) => annotatedFeatureIds.has(feature.id))
      ? { id: "note", label: "Campaign note", icon: "NotebookPen" as const }
      : null,
    visibleFeatures.some((feature) => influencedFeatureIds.has(feature.id))
      ? { id: "influence", label: "Selected faction", icon: "Flag" as const }
      : null,
  ].filter((entry): entry is NonNullable<typeof entry> => entry !== null);

  if (visibleLayers.length === 0 && states.length === 0) return null;

  return (
    <section aria-label="Map legend" className="map-legend">
      <span className="map-legend__label">
        <Icon name="ListTree" size={14} />
        Legend
      </span>
      <div className="map-legend__entries">
        {visibleLayers.map((layer, index) => (
          <span className="map-legend__entry" key={layer.id}>
            <i aria-hidden="true" data-layer-index={index % 4} />
            {layer.label}
            <small>{visibleFeatures.filter((feature) => feature.layerId === layer.id).length}</small>
          </span>
        ))}
        {states.map((entry) => (
          <span className="map-legend__entry map-legend__entry--state" key={entry.id}>
            <Icon name={entry.icon} size={13} />
            {entry.label}
          </span>
        ))}
      </div>
    </section>
  );
}
