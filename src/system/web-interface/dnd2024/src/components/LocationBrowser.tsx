import type { WorldLocation } from "../data/hub-types";
import { Icon } from "./Icon";

export function LocationBrowser({
  locations,
  query,
  selectedLocationId,
  currentLocationId,
  onQueryChange,
  onSelect,
}: {
  locations: WorldLocation[];
  query: string;
  selectedLocationId: string;
  currentLocationId: string;
  onQueryChange: (query: string) => void;
  onSelect: (locationId: string) => void;
}) {
  return (
    <section className="location-browser" aria-labelledby="location-browser-heading">
      <div className="location-browser__heading">
        <div>
          <span className="eyebrow">World atlas</span>
          <h2 id="location-browser-heading">Locations</h2>
        </div>
        <span>{locations.length} shown</span>
      </div>
      <label className="location-search">
        <span className="sr-only">Search locations</span>
        <Icon name="Search" size={17} />
        <input
          maxLength={80}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search places or regions"
          type="search"
          value={query}
        />
      </label>
      <div className="location-list" aria-label="Known world locations">
        {locations.length ? (
          locations.map((location) => {
            const selected = location.id === selectedLocationId;
            const current = location.id === currentLocationId;
            return (
              <button
                aria-pressed={selected}
                className="location-row"
                key={location.id}
                onClick={() => onSelect(location.id)}
                type="button"
              >
                <span className="location-row__mark"><Icon name="MapPin" size={17} /></span>
                <span className="location-row__copy">
                  <strong>{location.name}</strong>
                  <small>{location.region} · {location.kind}</small>
                </span>
                {current ? <em>Current</em> : <Icon name="ChevronRight" size={16} />}
              </button>
            );
          })
        ) : (
          <div className="location-empty">
            <Icon name="Search" />
            <strong>No matching places</strong>
            <p>Try a location name, region, or type.</p>
          </div>
        )}
      </div>
    </section>
  );
}
