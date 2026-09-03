import { useEffect, useState } from "react";
import type { CampaignMapOverlay, MapDocument, WorldReadModel } from "../data/hub-types";
import {
  buildMapBreadcrumbs,
  filterMapFeaturesByLayers,
  resolveFeatureOverlays,
  resolveMapChildScopes,
  resolveMapDocument,
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
  onMapChange,
  onNavigateToFeature,
  onFeatureSelect,
  onOpenLocation,
}: {
  world: WorldReadModel;
  activeMapId: string;
  selectedFeatureId: string;
  currentLocationId: string;
  campaignTitle: string;
  overlays: CampaignMapOverlay[];
  onMapChange: (mapId: string) => void;
  onNavigateToFeature: (mapId: string, featureId: string) => void;
  onFeatureSelect: (featureId: string) => void;
  onOpenLocation: (locationId: string) => void;
}) {
  const [hiddenLayerIdsByMap, setHiddenLayerIdsByMap] = useState<Record<string, string[]>>({});
  const [selectedFactionOverlayId, setSelectedFactionOverlayId] = useState("");
  const [viewMode, setViewMode] = useState<MapViewMode>("map");
  const [mapViews, setMapViews] = useState<Record<string, MapViewportState>>(restoredMapViews);
  const map = resolveMapDocument(world.maps, activeMapId) as MapDocument | null;

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
  const childScopes = resolveMapChildScopes(world.maps, map.id);
  const feature = resolveSelectedMapFeature(visibleMap, selectedFeatureId);
  const mapOverlays = resolveMapOverlays(overlays, map.id) as CampaignMapOverlay[];
  const annotatedFeatureIds = new Set<string>(
    mapOverlays
      .filter((overlay) => overlay.featureId !== null)
      .map((overlay) => overlay.featureId as string),
  );
  const scopeLinkFeatureIds = new Map<string, string>(
    map.scopeLinks
      .filter((link) => link.viaFeatureId !== null)
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
    const target = map.features.find((candidate) => candidate.id === featureId);
    if (target && hiddenLayerIds.has(target.layerId)) {
      setHiddenLayerIdsByMap((current) => ({
        ...current,
        [map.id]: (current[map.id] ?? []).filter((layerId) => layerId !== target.layerId),
      }));
    }
    onFeatureSelect(featureId);
  };

  return (
    <div className="world-map-view">
      <header className="atlas-heading world-map-heading">
        <div>
          <span className="eyebrow">Known geography</span>
          <h1 id="main-view-heading" tabIndex={-1}>{map.subject.name} map</h1>
        </div>
        <p>
          {visibleFeatures.length} of {map.features.length} visible {map.features.length === 1 ? "place" : "places"}
        </p>
      </header>

      <MapBreadcrumbs onSelect={onMapChange} trail={trail} />

      <MapAtlasSearch
        activeMapId={map.id}
        onNavigate={onNavigateToFeature}
        world={world}
      />

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

      <MapViewModeToggle mode={viewMode} onChange={setViewMode} />

      <div className="world-map-layout">
        {viewMode === "map" ? (
          <MapCanvas
            annotatedFeatureIds={annotatedFeatureIds}
            currentLocationId={currentLocationId}
            influencedFeatureIds={influencedFeatureIds}
            map={visibleMap}
            onFeatureSelect={selectFeature}
            onOpenScope={onMapChange}
            onViewportChange={(viewport) => setMapViews((current) => ({
              ...current,
              [map.id]: viewport,
            }))}
            scopeLinkFeatureIds={scopeLinkFeatureIds}
            selectedFeatureId={selectedFeatureId}
            viewport={mapViews[map.id] ?? DEFAULT_MAP_VIEWPORT}
          />
        ) : (
          <MapFeatureList
            annotatedFeatureIds={annotatedFeatureIds}
            currentLocationId={currentLocationId}
            influencedFeatureIds={influencedFeatureIds}
            map={visibleMap}
            onFeatureSelect={selectFeature}
            onOpenScope={onMapChange}
            scopeLinkFeatureIds={scopeLinkFeatureIds}
            selectedFeatureId={selectedFeatureId}
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
            onOpenScope={onMapChange}
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
          <MapScopeLinks childScopes={childScopes} onOpenScope={onMapChange} />
        </div>
      </div>
    </div>
  );
}
