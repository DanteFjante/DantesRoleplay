import { lazy, Suspense, type ReactNode } from "react";

import type {
  CampaignReadModel,
  LocationSectionId,
  Perspective,
  WorldLocation,
  WorldLocationScope,
  WorldReadModel,
  WorldSectionId,
} from "../data/hub-types";
import { LocationDirectory } from "./LocationDirectory";
import { LocationWorkspace } from "./LocationWorkspace";
import { WorldOverview } from "./WorldOverview";
import { WorldHistory } from "./WorldHistory";
import { WorldPeopleDirectory } from "./WorldPeopleDirectory";
import { WorldFactions } from "./WorldFactions";
import { WorldLore } from "./WorldLore";
import { WorldSectionNavigation } from "./WorldSectionNavigation";
import { PanelErrorBoundary } from "./PanelState";
import type { LorePageLoader } from "../data/world-lore-page";

const ScopedMapWorkspace = lazy(() => import("./ScopedMapWorkspace")
  .then((module) => ({ default: module.ScopedMapWorkspace })));

export function WorldView({
  deferredNotice,
  directoriesDeferred,
  loreLoading,
  loadLorePage,
  lorePageScope,
  section,
  activeMapId,
  selectedMapFeatureId,
  campaign,
  world,
  currentLocation,
  selectedLocation,
  locationScopeError,
  mapScopeState,
  mapScopeError,
  locationSection,
  perspective,
  selectedFactionId,
  selectedPersonId,
  onSectionChange,
  onMapChange,
  onMapNavigateToFeature,
  onMapFeatureSelect,
  onLocationSelect,
  onRetryLocationScope,
  onRetryMapScope,
  onLocationSectionChange,
  onFactionSelect,
  onPersonSelect,
  factionDirectoryBusy,
  onLoadMoreFactions,
  resetKey,
}: {
  deferredNotice?: ReactNode;
  directoriesDeferred?: boolean;
  loreLoading?: boolean;
  loadLorePage?: LorePageLoader;
  lorePageScope?: string;
  section: WorldSectionId;
  activeMapId: string;
  selectedMapFeatureId: string;
  campaign: CampaignReadModel;
  world: WorldReadModel;
  currentLocation: WorldLocation | null;
  selectedLocation: WorldLocation | null;
  filteredLocations: WorldLocation[];
  locationScope: WorldLocationScope | null;
  locationScopeBusy: boolean;
  locationScopeError: string;
  mapScopeState: "loading" | "ready" | "error";
  mapScopeError: string;
  locationSection: LocationSectionId;
  perspective: Perspective;
  selectedFactionId: string;
  selectedPersonId: string;
  query: string;
  onSectionChange: (section: WorldSectionId) => void;
  onMapChange: (mapId: string) => void;
  onMapNavigateToFeature: (mapId: string, featureId: string) => void;
  onMapFeatureSelect: (featureId: string) => void;
  onLocationSelect: (locationId: string) => void;
  onLocationBrowse: (locationId: string) => void;
  onLocationScopeBack: () => void;
  onLoadMoreLocations: () => void;
  onRetryLocationScope: () => void;
  onRetryMapScope: () => void;
  onLocationSectionChange: (section: LocationSectionId) => void;
  onFactionSelect: (factionId: string) => void;
  onPersonSelect: (personId: string) => void;
  factionDirectoryBusy?: boolean;
  onLoadMoreFactions?: () => void;
  onQueryChange: (query: string) => void;
  resetKey?: string;
}) {
  const selectedPeople = new Map((selectedLocation?.people ?? []).map((person) => [person.id, person]));
  for (const person of world.people) {
    selectedPeople.delete(person.id);
    if (person.location.id === selectedLocation?.id) {
      const { location: _location, ...details } = person;
      selectedPeople.set(person.id, details);
    }
  }
  const locationWithPeople = selectedLocation ? { ...selectedLocation, people: [...selectedPeople.values()] } : null;
  return (
    <div className="world-view">
      <WorldSectionNavigation activeSection={section} onSelect={onSectionChange} />
      {section === "overview" && directoriesDeferred ? <p role="status">
        World directories load when opened. Overview counts reflect only information loaded so far.
      </p> : null}
      {deferredNotice ?? <PanelErrorBoundary label={section === "overview" ? "World overview" : section}
        resetKey={`${resetKey ?? world.id}:${section}`}>
        {section === "overview" ? (
        <WorldOverview
          campaign={campaign}
          currentLocation={currentLocation}
          onBrowseLocations={() => onSectionChange("locations")}
          world={world}
        />
      ) : section === "map" ? (
        <Suspense fallback={<section aria-busy="true" className="view-loading" role="status">Opening map…</section>}>
          <ScopedMapWorkspace
            activeMapId={activeMapId}
            campaignTitle={campaign.title}
            overlays={campaign.mapOverlays}
            scopeError={mapScopeError}
            scopeState={mapScopeState}
            currentLocationId={world.currentLocationId}
            onFeatureSelect={onMapFeatureSelect}
            onMapChange={onMapChange}
            onNavigateToFeature={onMapNavigateToFeature}
            onOpenLocation={(locationId) => {
              onSectionChange("locations");
              onLocationSelect(locationId);
            }}
            onRetryScope={onRetryMapScope}
            selectedFeatureId={selectedMapFeatureId}
            world={world}
          />
        </Suspense>
      ) : section === "history" ? (
        <WorldHistory
          onOpenLocation={(locationId) => {
            onSectionChange("locations");
            onLocationSelect(locationId);
          }}
          world={world}
        />
      ) : section === "people" ? (
        <WorldPeopleDirectory
          onPersonSelect={onPersonSelect}
          onOpenLocation={(locationId) => {
            onSectionChange("locations");
            onLocationSelect(locationId);
            onLocationSectionChange("people");
          }}
          selectedPersonId={selectedPersonId}
          world={world}
        />
      ) : section === "factions" ? (
        <WorldFactions
          onFactionSelect={onFactionSelect}
          busy={factionDirectoryBusy}
          onLoadMore={onLoadMoreFactions}
          onOpenLocation={(locationId) => {
            onSectionChange("locations");
            onLocationSelect(locationId);
          }}
          selectedFactionId={selectedFactionId}
          world={world}
        />
      ) : section === "lore" ? (
        <WorldLore
          loading={loreLoading}
          loadPage={loadLorePage}
          pageScope={lorePageScope}
          onOpenFaction={(factionId) => {
            onFactionSelect(factionId);
            onSectionChange("factions");
          }}
          onOpenHistory={() => onSectionChange("history")}
          onOpenLocation={(locationId) => {
            onSectionChange("locations");
            onLocationSelect(locationId);
          }}
          world={world}
        />
      ) : (
        <div className="atlas-view">
          <header className="atlas-heading">
            <div>
              <span className="eyebrow">Browse the world</span>
              <h1 id="main-view-heading" tabIndex={-1}>Locations</h1>
            </div>
            <p>Select a place to see what is known about it.</p>
          </header>
          <div className="atlas-grid">
            <LocationDirectory
              locations={world.locations}
              currentLocationId={world.currentLocationId}
              onSelect={onLocationSelect}
              selectedLocationId={selectedLocation?.id ?? ""}
            />
            {locationScopeError ? <section role="alert"><p>{locationScopeError}</p>
              <button type="button" onClick={onRetryLocationScope}>Retry location details</button></section> : null}
            <LocationWorkspace
              location={locationWithPeople}
              onSectionChange={onLocationSectionChange}
              perspective={perspective}
              section={locationSection}
            />
          </div>
        </div>
      )}
      </PanelErrorBoundary>}
    </div>
  );
}
