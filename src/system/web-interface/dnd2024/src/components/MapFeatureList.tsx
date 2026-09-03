import type { MapDocument } from "../data/hub-types";
import { groupMapFeaturesByLayers } from "../state.js";
import { Icon } from "./Icon";

export function MapFeatureList({
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
  const groups = groupMapFeaturesByLayers(map) as Array<{
    layer: MapDocument["layers"][number];
    features: MapDocument["features"];
  }>;

  return (
    <section className="map-feature-list" aria-label={`${map.subject.name} places list`}>
      {groups.length === 0 ? (
        <div className="map-feature-list__empty">
          <Icon name="MapPin" size={22} />
          <strong>No places are visible in the selected layers.</strong>
          <p>Turn a layer back on to include its known places.</p>
        </div>
      ) : groups.map((group) => (
        <section key={group.layer.id} aria-labelledby={`${group.layer.id}-list-heading`}>
          <header>
            <h2 id={`${group.layer.id}-list-heading`}>{group.layer.label}</h2>
            <span>{group.features.length}</span>
          </header>
          <ul>
            {group.features.map((feature) => {
              const isCurrent = feature.locationId === currentLocationId;
              const childMapId = scopeLinkFeatureIds.get(feature.id);
              return (
                <li
                  data-annotated={annotatedFeatureIds.has(feature.id) ? "true" : undefined}
                  data-influenced={influencedFeatureIds.has(feature.id) ? "true" : undefined}
                  data-selected={selectedFeatureId === feature.id ? "true" : undefined}
                  key={feature.id}
                >
                  <button
                    aria-current={isCurrent ? "location" : undefined}
                    aria-pressed={selectedFeatureId === feature.id}
                    className="map-feature-list__select"
                    onClick={() => onFeatureSelect(feature.id)}
                    type="button"
                  >
                    <span className="map-feature-list__icon">
                      <Icon name={isCurrent ? "LocateFixed" : "MapPin"} size={16} />
                    </span>
                    <span>
                      <strong>{feature.name}</strong>
                      <small>{feature.detail}</small>
                      <span className="map-feature-list__badges">
                        {isCurrent ? <em>Current place</em> : null}
                        {annotatedFeatureIds.has(feature.id) ? <em>Campaign note</em> : null}
                        {influencedFeatureIds.has(feature.id) ? <em>Faction presence</em> : null}
                      </span>
                    </span>
                  </button>
                  {childMapId ? (
                    <button
                      className="map-feature-list__scope"
                      onClick={() => onOpenScope(childMapId)}
                      type="button"
                    >
                      Closer map <Icon name="ArrowRight" size={14} />
                    </button>
                  ) : null}
                </li>
              );
            })}
          </ul>
        </section>
      ))}
      <p className="world-map-panel__note">
        List and illustrated modes show the same authorized places and layer filters.
      </p>
    </section>
  );
}
