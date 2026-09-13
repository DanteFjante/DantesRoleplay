import { useState } from "react";
import type { MapSearchResult, WorldReadModel } from "../data/hub-types";
import { searchMapFeatures } from "../state.js";
import { Icon } from "./Icon";

const RESULT_LIMIT = 12;

export function MapAtlasSearch({
  world,
  activeMapId,
  onNavigate,
  onOpenLocation,
}: {
  world: WorldReadModel;
  activeMapId: string;
  onNavigate: (mapId: string, featureId: string) => void;
  onOpenLocation: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const mappedResults = searchMapFeatures(world.maps, query, activeMapId) as MapSearchResult[];
  const mappedLocationIds = new Set(mappedResults.map((result) => result.locationId));
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const results = [...mappedResults.map((result) => ({ ...result, target: "map" as const })),
    ...world.locations.filter((location) => normalizedQuery && !mappedLocationIds.has(location.id) &&
      [location.name, location.summary, location.region, location.kind].some((value) =>
        value.toLocaleLowerCase().includes(normalizedQuery)))
      .map((location) => ({ name: location.name, locationId: location.id, region: location.region, target: "location" as const }))]
    .sort((left, right) => left.name.localeCompare(right.name));
  const shownResults = results.slice(0, RESULT_LIMIT);
  const hasQuery = query.trim().length > 0;

  return (
    <section className="map-atlas-search" aria-label="Search this atlas">
      <label htmlFor="map-atlas-search-input">
        <Icon name="Search" size={15} />
        <span>Search loaded places</span>
      </label>
      <div className="map-atlas-search__field">
        <input
          autoComplete="off"
          id="map-atlas-search-input"
          onChange={(event) => setQuery(event.target.value.slice(0, 80))}
          placeholder="Search places and descriptions…"
          type="search"
          value={query}
        />
        {query ? (
          <button aria-label="Clear map search" onClick={() => setQuery("")} type="button">
            <Icon name="X" size={14} />
          </button>
        ) : null}
      </div>
      <p className="world-map-panel__note">Search includes the maps and locations opened so far. Browse closer areas to load more places.</p>
      {!hasQuery ? null : shownResults.length === 0 ? (
        <p className="map-atlas-search__empty">No loaded place matches that search.</p>
      ) : (
        <div className="map-atlas-search__results" role="list">
          {shownResults.map((result) => (
            <button
              key={result.target === "map" ? `${result.mapId}:${result.featureId}` : `location:${result.locationId}`}
              onClick={() => {
                if (result.target === "map") onNavigate(result.mapId, result.featureId);
                else onOpenLocation(result.locationId);
                setQuery("");
              }}
              role="listitem"
              type="button"
            >
              <span>
                <strong>{result.name}</strong>
                <small>{result.target === "map" ? `${result.mapName} · ${result.mapScope} map` : `${result.region} · location details`}</small>
              </span>
              <Icon name="ArrowRight" size={15} />
            </button>
          ))}
          {results.length > shownResults.length ? (
            <p>Showing {shownResults.length} of {results.length} matches. Refine the search to see more.</p>
          ) : null}
        </div>
      )}
    </section>
  );
}
