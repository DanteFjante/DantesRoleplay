import type { CampaignMapOverlay, MapDocument, MapFeature } from "../data/hub-types";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";

export function MapFeatureDetail({
  map,
  feature,
  childMapId,
  overlays,
  influenceFactionNames,
  onOpenScope,
  onOpenLocation,
}: {
  map: MapDocument;
  feature: MapFeature | null;
  childMapId: string | null;
  overlays: CampaignMapOverlay[];
  influenceFactionNames: string[];
  onOpenScope: (mapId: string) => void;
  onOpenLocation: (locationId: string) => void;
}) {
  return (
    <aside className="panel world-map-selection" aria-live="polite">
      <span className="eyebrow">Selected place</span>
      {feature === null ? (
        <p>Select a marker to read what is known about it.</p>
      ) : (
        <>
          {feature.preview ? (
            <figure className="world-map-selection__media">
              <MediaImage fallback={null} media={feature.preview} />
            </figure>
          ) : null}
          <div className="world-map-selection__title">
            <span aria-hidden="true"><Icon name="Landmark" size={22} /></span>
            <div>
              <h2>{feature.name}</h2>
              <p>{map.subject.name}</p>
            </div>
          </div>
          <p>{feature.detail}</p>
          {influenceFactionNames.length ? (
            <p className="map-feature-influence">
              <Icon name="Shield" size={15} />
              Recorded presence: {influenceFactionNames.join(", ")}
            </p>
          ) : null}
          {overlays.length === 0 ? null : (
            <ul className="map-feature-overlays">
              {overlays.map((overlay) => (
                <li key={overlay.id}>
                  <strong>{overlay.label}</strong>
                  <span>{overlay.detail}</span>
                </li>
              ))}
            </ul>
          )}
          {childMapId ? (
            <button className="text-action" onClick={() => onOpenScope(childMapId)} type="button">
              Open the closer map <Icon name="ArrowRight" size={16} />
            </button>
          ) : null}
          {feature.locationId ? (
            <button
              className="text-action"
              onClick={() => onOpenLocation(feature.locationId as string)}
              type="button"
            >
              Open location details <Icon name="ArrowRight" size={16} />
            </button>
          ) : null}
        </>
      )}
    </aside>
  );
}
