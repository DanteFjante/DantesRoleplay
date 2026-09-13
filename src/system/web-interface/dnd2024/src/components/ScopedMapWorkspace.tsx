import { useEffect, useState } from "react";
import type { CampaignMapOverlay, MapChildScope, MapDocument, WorldReadModel } from "../data/hub-types";
import {
  buildMapBreadcrumbs,
  filterMapFeaturesByLayers,
  resolveFeatureOverlays,
  resolveMapChildScopes,
  resolveMapDocument,
  resolveMapNavigation,
  resolveMapOverlays,
  resolveMapFactionInfluences,
  resolveSelectedMapFeature,
} from "../state.js";
import { MapBreadcrumbs } from "./MapBreadcrumbs";
import { DEFAULT_MAP_VIEWPORT, MapCanvas, type MapViewportState } from "./MapCanvas";
import { MapFeatureDetail } from "./MapFeatureDetail";
import { MapLayerControls } from "./MapLayerControls";
import { MapLegend } from "./MapLegend";
import { MapAtlasSearch } from "./MapAtlasSearch";
import { MapFactionInfluenceControls } from "./MapFactionInfluenceControls";
import { MapFeatureList } from "./MapFeatureList";
import { MapViewModeToggle, type MapViewMode } from "./MapViewModeToggle";
import { MapOverlayNotes } from "./MapOverlayNotes";
import { MapScopeLinks } from "./MapScopeLinks";

const MAP_VIEW_SESSION_KEY = "dantesroleplay.dnd2024.map-views.v1";

function restoredMapViews(): Record<string, MapViewportState> {
  if (typeof window === "undefined") return {};
  try {
    const parsed = JSON.parse(window.sessionStorage.getItem(MAP_VIEW_SESSION_KEY) ?? "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return {};
    return Object.fromEntries(Object.entries(parsed).flatMap(([mapId, value]) => {
      const view = value as Partial<MapViewportState>;
      return typeof mapId === "string" && mapId.length <= 200 &&
        Number.isFinite(view.zoom) && Number.isFinite(view.x) && Number.isFinite(view.y) &&
        view.zoom! >= 0.5 && view.zoom! <= 4
        ? [[mapId, { zoom: view.zoom!, x: view.x!, y: view.y! }]]
        : [];
    }));
  } catch {
    return {};
  }
}

export function ScopedMapWorkspace({
  world,
  activeMapId,
  selectedFeatureId,
  currentLocationId,
  campaignTitle,
  overlays,
  scopeState,
  scopeError,
  onMapChange,
  onNavigateToFeature,
  onFeatureSelect,
  onOpenLocation,
  onRetryScope,
}: {
  world: WorldReadModel;
  activeMapId: string;
  selectedFeatureId: string;
  currentLocationId: string;
  campaignTitle: string;
  overlays: CampaignMapOverlay[];
  scopeState: "loading" | "ready" | "error";
  scopeError: string;
  onMapChange: (mapId: string) => void;
  onNavigateToFeature: (mapId: string, featureId: string) => void;
  onFeatureSelect: (featureId: string) => void;
  onOpenLocation: (locationId: string) => void;
  onRetryScope: () => void;
}) {
  const [hiddenLayerIdsByMap, setHiddenLayerIdsByMap] = useState<Record<string, string[]>>({});
  const [selectedFactionOverlayId, setSelectedFactionOverlayId] = useState("");
  const [viewMode, setViewMode] = useState<MapViewMode>("map");
  const [mapViews, setMapViews] = useState<Record<string, MapViewportState>>(restoredMapViews);
  const [parentMapNotice, setParentMapNotice] = useState<{ requestedMapId: string; shownMapId: string } | null>(null);
  const map = resolveMapDocument(world.maps, activeMapId) as MapDocument | null;

  useEffect(() => {
    if (scopeState === "loading") return;
    const target = resolveMapNavigation(world.maps, activeMapId, selectedFeatureId);
    if (target.mapId !== activeMapId) {
      setParentMapNotice({ requestedMapId: activeMapId, shownMapId: target.mapId });
      onNavigateToFeature(target.mapId, target.featureId);
    } else setParentMapNotice((current) => current?.shownMapId === activeMapId ? current : null);
  }, [world.maps, activeMapId, selectedFeatureId, onNavigateToFeature, scopeState]);

  useEffect(() => {
    try { window.sessionStorage.setItem(MAP_VIEW_SESSION_KEY, JSON.stringify(mapViews)); }
    catch { /* View persistence is optional and never game-state authority. */ }
  }, [mapViews]);

  if (map === null) {
    return (
      <div className="world-map-view">
        <header className="atlas-heading world-map-heading">
          <div>
            <span className="eyebrow">Known geography</span>
            <h1 id="main-view-heading" tabIndex={-1}>{world.name} maps</h1>
          </div>
        </header>
        <p className="map-base-absent">Map not available for this scope.</p>
      </div>
    );
  }

  const hiddenLayerIds = new Set(hiddenLayerIdsByMap[map.id] ?? []);
  const requestedMap = parentMapNotice?.shownMapId === map.id
    ? resolveMapDocument(world.maps, parentMapNotice.requestedMapId) as MapDocument | null : null;
  const visibleLayerIds = new Set(
    map.layers.filter((layer) => !hiddenLayerIds.has(layer.id)).map((layer) => layer.id),
  );
  const visibleFeatures = filterMapFeaturesByLayers(map, visibleLayerIds);
  const visibleMap = { ...map, features: visibleFeatures } as MapDocument;
  const factionInfluences = resolveMapFactionInfluences(world.factions, map);
  const activeFactionInfluence = factionInfluences.find(
    (influence) => influence.factionId === selectedFactionOverlayId,
  ) ?? null;
  const influencedFeatureIds = new Set<string>(activeFactionInfluence?.featureIds ?? []);
  const trail = buildMapBreadcrumbs(world.maps, map.id);
  const childScopes = (resolveMapChildScopes(world.maps, map.id) as MapChildScope[])
    .filter((child) => child.baseState !== "absent");
  const directChildIds = new Set(world.locationScopes.find((scope) => scope.id === map.subject.id)?.childIds ?? []);
  const plottedLocationIds = new Set(map.features.map((entry) => entry.locationId));
  const unplacedLocations = scopeState === "loading" ? [] : world.locations.filter((location) =>
    (directChildIds.has(location.id) || location.parentId === map.subject.id) && !plottedLocationIds.has(location.id));
  const childMapLocationIds = new Set(childScopes.map((child) =>
    world.maps.find((entry) => entry.id === child.mapId)?.subject.id));
  const feature = resolveSelectedMapFeature(visibleMap, selectedFeatureId);
  const mapOverlays = resolveMapOverlays(overlays, map.id) as CampaignMapOverlay[];
  const annotatedFeatureIds = new Set<string>(
    mapOverlays
      .filter((overlay) => overlay.featureId !== null)
      .map((overlay) => overlay.featureId as string),
  );
  const scopeLinkFeatureIds = new Map<string, string>(
    map.scopeLinks
      .filter((link) => link.viaFeatureId !== null &&
        resolveMapDocument(world.maps, link.childMapId)?.baseState !== "absent")
      .map((link) => [link.viaFeatureId as string, link.childMapId]),
  );

  const toggleLayer = (layerId: string) => {
    const isVisible = visibleLayerIds.has(layerId);
    setHiddenLayerIdsByMap((current) => {
      const hidden = new Set(current[map.id] ?? []);
      if (isVisible) hidden.add(layerId);
      else hidden.delete(layerId);
      return { ...current, [map.id]: [...hidden] };
    });
    const selectedFeature = map.features.find((candidate) => candidate.id === selectedFeatureId);
    if (isVisible && selectedFeature?.layerId === layerId) onFeatureSelect("");
  };

  const selectFeature = (featureId: string) => {
    setParentMapNotice(null);
    const target = map.features.find((candidate) => candidate.id === featureId);
    if (target && hiddenLayerIds.has(target.layerId)) {
      setHiddenLayerIdsByMap((current) => ({
        ...current,
        [map.id]: (current[map.id] ?? []).filter((layerId) => layerId !== target.layerId),
      }));
    }
    onFeatureSelect(featureId);
  };
  const changeMap = (mapId: string) => {
    setParentMapNotice(null);
    onMapChange(mapId);
  };

  return (
    <div className="world-map-view">
      <header className="atlas-heading world-map-heading">
        <div>
          <span className="eyebrow">Known geography</span>
          <h1 id="main-view-heading" tabIndex={-1}>{map.subject.name} map</h1>
        </div>
        {scopeState === "loading" ? <p>Loading map locations…</p> : <p>
          {visibleFeatures.length} of {map.features.length} map {map.features.length === 1 ? "marker" : "markers"} shown
          {unplacedLocations.length ? <> · {unplacedLocations.length} known {unplacedLocations.length === 1 ? "place" : "places"} without map positions</> : null}
        </p>}
      </header>

      <MapBreadcrumbs onSelect={changeMap} trail={trail} />
      {requestedMap ? <p className="map-scope-status" role="status">
        Showing {map.subject.name} for {requestedMap.subject.name}. {requestedMap.baseState === "unavailable"
          ? "Its local map could not be loaded." : "This place has no separate map."}
      </p> : null}

      {scopeState === "loading" ? (
        <section aria-busy="true" className="map-scope-status" role="status">
          Loading the places on this map…
        </section>
      ) : scopeState === "error" ? (
        <section className="map-scope-status map-scope-status--error" role="alert">
          <div>
            <strong>The places on this map could not be loaded.</strong>
            <span>{scopeError}</span>
          </div>
          <button onClick={onRetryScope} type="button">Retry places</button>
        </section>
      ) : null}

      <MapAtlasSearch
        activeMapId={map.id}
        onNavigate={(mapId, featureId) => { setParentMapNotice(null); onNavigateToFeature(mapId, featureId); }}
        onOpenLocation={onOpenLocation}
        world={world}
      />

      <div className="map-display-options">
        <details className="map-display-options__details">
          <summary>Layers and legend</summary>
      <MapLayerControls
        features={map.features}
        layers={map.layers}
        onToggle={toggleLayer}
        visibleLayerIds={visibleLayerIds}
      />

      <MapFactionInfluenceControls
        influences={factionInfluences}
        onSelect={setSelectedFactionOverlayId}
        selectedFactionId={activeFactionInfluence?.factionId ?? ""}
      />

      <MapLegend
        annotatedFeatureIds={annotatedFeatureIds}
        currentLocationId={currentLocationId}
        features={map.features}
        influencedFeatureIds={influencedFeatureIds}
        layers={map.layers}
        scopeLinkFeatureIds={scopeLinkFeatureIds}
        visibleLayerIds={visibleLayerIds}
      />
        </details>
      <MapViewModeToggle mode={viewMode} onChange={setViewMode} />
      </div>

      <div className="world-map-layout">
        {scopeState === "loading" && (viewMode === "list" || !map.base) ? (
          <section className={viewMode === "list" ? "map-feature-list" : "world-map-panel"} aria-busy="true">
            <p>Loading map locations…</p>
          </section>
        ) : viewMode === "map" ? (
          <MapCanvas
            annotatedFeatureIds={annotatedFeatureIds}
            currentLocationId={currentLocationId}
            influencedFeatureIds={influencedFeatureIds}
            map={visibleMap}
            onFeatureSelect={selectFeature}
            onOpenScope={changeMap}
            onViewportChange={(viewport) => setMapViews((current) => ({
              ...current,
              [map.id]: viewport,
            }))}
            scopeLinkFeatureIds={scopeLinkFeatureIds}
            selectedFeatureId={selectedFeatureId}
            viewport={mapViews[map.id] ?? DEFAULT_MAP_VIEWPORT}
            readyToReport={scopeState === "ready"}
          />
        ) : (
          <MapFeatureList
            annotatedFeatureIds={annotatedFeatureIds}
            currentLocationId={currentLocationId}
            influencedFeatureIds={influencedFeatureIds}
            map={visibleMap}
            onFeatureSelect={selectFeature}
            onOpenScope={changeMap}
            scopeLinkFeatureIds={scopeLinkFeatureIds}
            selectedFeatureId={selectedFeatureId}
            unplacedLocations={unplacedLocations}
            onOpenLocation={onOpenLocation}
          />
        )}
        <div className="map-side-column">
          <MapFeatureDetail
            childMapId={feature ? (scopeLinkFeatureIds.get(feature.id) ?? null) : null}
            feature={feature}
            influenceFactionNames={
              feature && influencedFeatureIds.has(feature.id) && activeFactionInfluence
                ? [activeFactionInfluence.name]
                : []
            }
            map={map}
            onOpenLocation={onOpenLocation}
            onOpenScope={changeMap}
            overlays={
              feature
                ? (resolveFeatureOverlays(overlays, map.id, feature.id) as CampaignMapOverlay[])
                : []
            }
          />
          <MapOverlayNotes
            campaignTitle={campaignTitle}
            onSelectFeature={selectFeature}
            overlays={mapOverlays}
          />
          {scopeState !== "loading" ? <MapScopeLinks childScopes={childScopes} onOpenScope={changeMap}
            unplacedLocations={unplacedLocations.filter((location) => !childMapLocationIds.has(location.id))}
            onOpenLocation={onOpenLocation} /> : null}
        </div>
      </div>
    </div>
  );
}
