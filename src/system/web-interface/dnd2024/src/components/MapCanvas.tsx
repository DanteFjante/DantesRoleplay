import type { MapDocument, MapFeature } from "../data/hub-types";
import { Icon } from "./Icon";

/**
 * Placement converts a feature's geometry into a percentage of its own declaring map's
 * coordinate space. No value is ever carried between two different scopes' spaces.
 */
function placement(feature: MapFeature, map: MapDocument) {
  return {
    left: `${(feature.geometry.x / map.coordinateSpace.width) * 100}%`,
    top: `${(feature.geometry.y / map.coordinateSpace.height) * 100}%`,
  };
}

export function MapCanvas({
  map,
  selectedFeatureId,
  currentLocationId,
  scopeLinkFeatureIds,
  annotatedFeatureIds,
  influencedFeatureIds,
  onFeatureSelect,
  onOpenScope,
}: {
  map: MapDocument;
  selectedFeatureId: string;
  currentLocationId: string;
  scopeLinkFeatureIds: Map<string, string>;
  annotatedFeatureIds: Set<string>;
  influencedFeatureIds: Set<string>;
  onFeatureSelect: (featureId: string) => void;
  onOpenScope: (mapId: string) => void;
}) {
  return (
    <section className="world-map-panel" aria-label={`${map.subject.name} map`}>
      <div className="world-map-canvas" data-base={map.base ? "present" : "absent"}>
        {map.base ? (
          <img alt={map.base.alt} src={map.base.imageUrl} />
        ) : (
          <p className="map-base-absent">
            <Icon name="Map" size={20} />
            Map not available for {map.subject.name}. The places below remain readable as
            information.
          </p>
        )}
        <div className="world-map-markers" aria-label={`${map.subject.name} places`}>
          {map.features.map((feature) => {
            const childMapId = scopeLinkFeatureIds.get(feature.id);
            const isCurrent = feature.locationId !== null && feature.locationId === currentLocationId;
            return (
              <button
                aria-label={`${feature.name}${isCurrent ? ", current location" : ""}${
                  childMapId ? ", opens a closer map" : ""
                }`}
                aria-pressed={feature.id === selectedFeatureId}
                className="world-map-marker"
                data-current={isCurrent ? "true" : undefined}
                data-annotated={annotatedFeatureIds.has(feature.id) ? "true" : undefined}
                data-influenced={influencedFeatureIds.has(feature.id) ? "true" : undefined}
                data-scope-link={childMapId ? "true" : undefined}
                key={feature.id}
                onClick={() => onFeatureSelect(feature.id)}
                onDoubleClick={childMapId ? () => onOpenScope(childMapId) : undefined}
                style={placement(feature, map)}
                type="button"
              >
                <span className="world-map-marker__pin" aria-hidden="true">
                  <Icon name={isCurrent ? "LocateFixed" : childMapId ? "Landmark" : "MapPin"} size={15} />
                </span>
                <span aria-hidden="true" className="world-map-marker__label">
                  {feature.name}
                </span>
              </button>
            );
          })}
        </div>
      </div>
      <p className="world-map-panel__note">
        Placement is illustrative within this scope only. It calculates no distance, route, or
        travel time, and each scope keeps its own coordinates.
      </p>
    </section>
  );
}
