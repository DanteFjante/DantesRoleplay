import type { WorldLocation, WorldLocationScope } from "../data/hub-types";
import { Icon } from "./Icon";

export function LocationBrowser({
  locations,
  locationScope,
  busy,
  error,
  query,
  selectedLocationId,
  currentLocationId,
  onBack,
  onLoadMore,
  onQueryChange,
  onRetry,
  onSelect,
}: {
  locations: WorldLocation[];
  locationScope: WorldLocationScope | null;
  busy: boolean;
  error: string;
  query: string;
  selectedLocationId: string;
  currentLocationId: string;
  onBack: () => void;
  onLoadMore: () => void;
  onQueryChange: (query: string) => void;
  onRetry: () => void;
  onSelect: (locationId: string) => void;
}) {
  return (
    <section className="location-browser" aria-labelledby="location-browser-heading">
      <div className="location-browser__heading">
        <div>
          <span className="eyebrow">This location level</span>
          <h2 id="location-browser-heading">{locationScope?.name ?? "Locations"}</h2>
        </div>
        <span>{locationScope
          ? `${locationScope.childIds.length} of ${locationScope.totalCount}`
          : "Not loaded"}</span>
      </div>
      {locationScope?.parentId ? (
        <button className="location-browser__back" onClick={onBack} type="button">
          <Icon name="ArrowLeft" size={16} /> Parent location
        </button>
      ) : null}
      <label className="location-search">
        <span className="sr-only">Search this location level</span>
        <Icon name="Search" size={17} />
        <input
          maxLength={80}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search this level"
          type="search"
          value={query}
        />
      </label>
      <p className="location-search__scope">Search is limited to the locations shown in this level.</p>
      {error ? (
        <div className="location-browser__error" role="alert">
          <p>{error}</p>
          <button onClick={onRetry} type="button">Refresh this level</button>
        </div>
      ) : null}
      <div className="location-list" aria-label="Known world locations">
        {locations.length ? (
          locations.map((location) => {
            const selected = location.id === selectedLocationId;
            const current = location.id === currentLocationId;
            return (
              <button
                aria-pressed={selected}
                aria-label={`Open ${location.name} and browse its locations`}
                className="location-row"
                data-record-id={location.id}
                key={location.id}
                onClick={() => onSelect(location.id)}
                type="button"
              >
                <span className="location-row__mark"><Icon name="MapPin" size={17} /></span>
                <span className="location-row__copy">
                  <strong>{location.name}</strong>
                  <small>{location.region} · {location.kind}</small>
                </span>
                <span className="location-row__action">
                  {current ? <em>Current</em> : null}
                  <small>Open</small>
                  <Icon name="ChevronRight" size={16} />
                </span>
              </button>
            );
          })
        ) : (
          <div className="location-empty">
            <Icon name="Search" />
            <strong>{query ? "No matching places" : "No child locations"}</strong>
            <p>{query ? "Try a location name, region, or type." : "This place has no recorded locations inside it."}</p>
          </div>
        )}
      </div>
      {locationScope?.nextCursor ? (
        <button className="location-browser__more" disabled={busy} onClick={onLoadMore} type="button">
          {busy ? "Loading more…" : `Load more locations (${locationScope.childIds.length} of ${locationScope.totalCount})`}
        </button>
      ) : busy ? <p aria-live="polite" className="location-browser__busy">Loading this location level…</p> : null}
    </section>
  );
}
