import { useState } from "react";
import type { MapSearchResult, WorldReadModel } from "../data/hub-types";
import { searchMapFeatures } from "../state.js";
import { Icon } from "./Icon";

const RESULT_LIMIT = 12;

export function MapAtlasSearch({
  world,
  activeMapId,
  onNavigate,
}: {
  world: WorldReadModel;
  activeMapId: string;
  onNavigate: (mapId: string, featureId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const results = searchMapFeatures(world.maps, query, activeMapId) as MapSearchResult[];
  const shownResults = results.slice(0, RESULT_LIMIT);
  const hasQuery = query.trim().length > 0;

  return (
    <section className="map-atlas-search" aria-label="Search this atlas">
      <label htmlFor="map-atlas-search-input">
        <Icon name="Search" size={15} />
        <span>Find a place across every known map</span>
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
      {!hasQuery ? null : shownResults.length === 0 ? (
        <p className="map-atlas-search__empty">No known mapped place matches that search.</p>
      ) : (
        <div className="map-atlas-search__results" role="list">
          {shownResults.map((result) => (
            <button
              key={`${result.mapId}:${result.featureId}`}
              onClick={() => {
                onNavigate(result.mapId, result.featureId);
                setQuery("");
              }}
              role="listitem"
              type="button"
            >
              <span>
                <strong>{result.name}</strong>
                <small>{result.mapName} · {result.mapScope} map</small>
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
