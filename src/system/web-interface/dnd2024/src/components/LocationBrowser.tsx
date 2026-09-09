import { useEffect, useState } from "react";

import type { WorldLocation, WorldLocationScope } from "../data/hub-types";
import { Icon } from "./Icon";

type LocationBrowserMode = "all" | "level";

export function LocationBrowser({
  locations,
  allLocations,
  locationScopes,
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
  onBrowse,
}: {
  locations: WorldLocation[];
  allLocations: WorldLocation[];
  locationScopes: WorldLocationScope[];
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
  onBrowse: (locationId: string) => void;
}) {
  const [mode, setMode] = useState<LocationBrowserMode>("all");
  useEffect(() => {
    if (locationScope?.parentId) setMode("level");
  }, [locationScope?.id, locationScope?.parentId]);
  const visible = mode === "all" ? allLocations : locations;
  const scopeById = new Map(locationScopes.map((scope) => [scope.id, scope]));
  const locationById = new Map(allLocations.map((location) => [location.id, location]));
  const locationContext = (location: WorldLocation) => {
    const parent = location.parentId ? locationById.get(location.parentId) : null;
    return parent ? `${parent.name} · ${location.kind}` : `${location.region} · ${location.kind}`;
  };

  return (
    <section className="location-browser" aria-labelledby="location-browser-heading">
      <div className="location-browser__heading">
        <div>
          <span className="eyebrow">{mode === "all" ? "Complete directory" : "This location level"}</span>
          <h2 id="location-browser-heading">{mode === "all" ? "All known locations" : locationScope?.name ?? "Locations"}</h2>
        </div>
        <span>{mode === "all"
          ? `${allLocations.length} ${allLocations.length === 1 ? "place" : "places"}`
          : locationScope
          ? `${locationScope.childIds.length} of ${locationScope.totalCount}`
          : "Not loaded"}</span>
      </div>
      <div aria-label="Location browsing mode" className="location-browser__modes" role="group">
        <button aria-pressed={mode === "all"} onClick={() => setMode("all")} type="button">
          <Icon name="List" size={15} /> All places
        </button>
        <button aria-pressed={mode === "level"} onClick={() => setMode("level")} type="button">
          <Icon name="Network" size={15} /> By area
        </button>
      </div>
      {mode === "level" && locationScope?.parentId ? (
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
          placeholder={mode === "all" ? "Search every known location" : "Search this area"}
          type="search"
          value={query}
        />
      </label>
      <p className="location-search__scope">{mode === "all"
        ? "Select a place for details, or browse inside an area to follow the hierarchy."
        : "Only the direct locations inside this area are shown."}</p>
      {error ? (
        <div className="location-browser__error" role="alert">
          <p>{error}</p>
          <button onClick={onRetry} type="button">Refresh this level</button>
        </div>
      ) : null}
      <div className="location-list" aria-label="Known world locations">
        {visible.length ? (
          visible.map((location) => {
            const selected = location.id === selectedLocationId;
            const current = location.id === currentLocationId;
            const childScope = scopeById.get(location.id);
            const childCount = childScope?.totalCount ?? 0;
            const canBrowse = childCount > 0 || childScope === undefined;
            return (
              <div className="location-row-group" data-record-id={location.id} key={location.id}>
                <button
                  aria-pressed={selected}
                  aria-label={`View details for ${location.name}`}
                  className="location-row"
                  onClick={() => onSelect(location.id)}
                  type="button"
                >
                  <span className="location-row__mark"><Icon name="MapPin" size={17} /></span>
                  <span className="location-row__copy">
                    <strong>{location.name}</strong>
                    <small>{locationContext(location)}</small>
                  </span>
                  <span className="location-row__action">
                    {current ? <em>Current</em> : null}
                    <small>Details</small>
                    <Icon name="ChevronRight" size={16} />
                  </span>
                </button>
                {canBrowse ? (
                  <button aria-label={`Open ${location.name} and browse its locations`}
                    className="location-row__browse" onClick={() => {
                      setMode("level");
                      onBrowse(location.id);
                    }} type="button">
                    Browse inside {childScope ? <span>{childCount}</span> : null}
                  </button>
                ) : null}
              </div>
            );
          })
        ) : (
          <div className="location-empty">
            <Icon name="Search" />
            <strong>{query ? "No matching places" : mode === "all" ? "No known locations" : "No child locations"}</strong>
            <p>{query ? "Try a location name, region, or type."
              : mode === "all" ? "No locations are available in this directory."
                : "This place has no recorded locations inside it."}</p>
          </div>
        )}
      </div>
      {mode === "level" && locationScope?.nextCursor ? (
        <button className="location-browser__more" disabled={busy} onClick={onLoadMore} type="button">
          {busy ? "Loading more…" : `Load more locations (${locationScope.childIds.length} of ${locationScope.totalCount})`}
        </button>
      ) : mode === "level" && busy
        ? <p aria-live="polite" className="location-browser__busy">Loading this location level…</p> : null}
    </section>
  );
}
