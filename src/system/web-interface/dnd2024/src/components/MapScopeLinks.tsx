import type { MapChildScope } from "../data/hub-types";
import { Icon } from "./Icon";

const SCOPE_LABELS: Record<string, string> = {
  world: "World",
  region: "Region",
  city: "City",
  location: "Location",
};

const SCOPE_ICONS: Record<string, string> = {
  world: "Globe2",
  region: "Mountain",
  city: "Castle",
  location: "Landmark",
};

export function MapScopeLinks({
  childScopes,
  onOpenScope,
}: {
  childScopes: MapChildScope[];
  onOpenScope: (mapId: string) => void;
}) {
  return (
    <section className="panel map-scope-links" aria-label="Closer areas">
      <span className="eyebrow">Closer areas</span>
      {childScopes.length === 0 ? (
        <p className="map-scope-links__empty">
          No closer area is loaded from this scope.
        </p>
      ) : (
        <ul>
          {childScopes.map((child) => (
            <li key={child.id}>
              <button onClick={() => onOpenScope(child.mapId)} type="button">
                <span aria-hidden="true"><Icon name={SCOPE_ICONS[child.scope] ?? "MapPin"} size={16} /></span>
                <span>
                  <strong>{child.name}</strong>
                  <small>{SCOPE_LABELS[child.scope] ?? child.scope}{child.baseState === "ready"
                    ? " map"
                    : child.baseState === "unavailable" ? " · map unavailable" : " · list view"}</small>
                </span>
                <Icon name="ArrowRight" size={16} />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
