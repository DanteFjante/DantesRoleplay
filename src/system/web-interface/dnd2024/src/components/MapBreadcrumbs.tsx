import type { MapBreadcrumb } from "../data/hub-types";
import { Icon } from "./Icon";

const SCOPE_ICONS: Record<string, string> = {
  world: "Globe2",
  region: "Mountain",
  city: "Castle",
  location: "Landmark",
};

export function MapBreadcrumbs({
  trail,
  onSelect,
}: {
  trail: MapBreadcrumb[];
  onSelect: (mapId: string) => void;
}) {
  if (trail.length === 0) return null;

  return (
    <nav aria-label="Map scope" className="map-breadcrumbs">
      <ol>
        {trail.map((crumb, index) => {
          const isCurrent = index === trail.length - 1;
          return (
            <li key={crumb.id}>
              {index > 0 ? (
                <span aria-hidden="true" className="map-breadcrumbs__separator">
                  <Icon name="ChevronRight" size={14} />
                </span>
              ) : null}
              {isCurrent ? (
                <span aria-current="page" className="map-breadcrumbs__current">
                  <Icon name={SCOPE_ICONS[crumb.scope] ?? "MapPin"} size={15} />
                  {crumb.name}
                </span>
              ) : (
                <button onClick={() => onSelect(crumb.id)} type="button">
                  <Icon name={SCOPE_ICONS[crumb.scope] ?? "MapPin"} size={15} />
                  {crumb.name}
                </button>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
