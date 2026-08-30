import type {
  CampaignReadModel,
  LocationSectionId,
  Perspective,
  WorldLocation,
  WorldReadModel,
  WorldSectionId,
} from "../data/hub-types";
import { LocationBrowser } from "./LocationBrowser";
import { LocationWorkspace } from "./LocationWorkspace";
import { WorldOverview } from "./WorldOverview";
import { ScopedMapWorkspace } from "./ScopedMapWorkspace";
import { WorldHistory } from "./WorldHistory";
import { WorldPeopleDirectory } from "./WorldPeopleDirectory";
import { WorldFactions } from "./WorldFactions";
import { WorldLore } from "./WorldLore";
import { WorldSectionNavigation } from "./WorldSectionNavigation";

export function WorldView({
  section,
  activeMapId,
  selectedMapFeatureId,
  campaign,
  world,
  currentLocation,
  selectedLocation,
  filteredLocations,
  locationSection,
  perspective,
  selectedFactionId,
  selectedPersonId,
  query,
  onSectionChange,
  onMapChange,
  onMapNavigateToFeature,
  onMapFeatureSelect,
  onLocationSelect,
  onLocationSectionChange,
  onFactionSelect,
  onQueryChange,
}: {
  section: WorldSectionId;
  activeMapId: string;
  selectedMapFeatureId: string;
  campaign: CampaignReadModel;
  world: WorldReadModel;
  currentLocation: WorldLocation;
  selectedLocation: WorldLocation;
  filteredLocations: WorldLocation[];
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
  onLocationSectionChange: (section: LocationSectionId) => void;
  onFactionSelect: (factionId: string) => void;
  onQueryChange: (query: string) => void;
}) {
  return (
    <div className="world-view">
      <WorldSectionNavigation activeSection={section} onSelect={onSectionChange} />
      {section === "overview" ? (
        <WorldOverview
          campaign={campaign}
          currentLocation={currentLocation}
          onBrowseLocations={() => onSectionChange("locations")}
          world={world}
        />
      ) : section === "map" ? (
        <ScopedMapWorkspace
          activeMapId={activeMapId}
          campaignTitle={campaign.title}
          overlays={campaign.mapOverlays}
          currentLocationId={currentLocation.id}
          onFeatureSelect={onMapFeatureSelect}
          onMapChange={onMapChange}
          onNavigateToFeature={onMapNavigateToFeature}
          onOpenLocation={(locationId) => {
            onLocationSelect(locationId);
            onSectionChange("locations");
          }}
          selectedFeatureId={selectedMapFeatureId}
          world={world}
        />
      ) : section === "history" ? (
        <WorldHistory
          onOpenLocation={(locationId) => {
            onLocationSelect(locationId);
            onSectionChange("locations");
          }}
          world={world}
        />
      ) : section === "people" ? (
        <WorldPeopleDirectory
          onOpenLocation={(locationId) => {
            onLocationSelect(locationId);
            onLocationSectionChange("people");
            onSectionChange("locations");
          }}
          selectedPersonId={selectedPersonId}
          world={world}
        />
      ) : section === "factions" ? (
        <WorldFactions
          onFactionSelect={onFactionSelect}
          onOpenLocation={(locationId) => {
            onLocationSelect(locationId);
            onSectionChange("locations");
          }}
          selectedFactionId={selectedFactionId}
          world={world}
        />
      ) : section === "lore" ? (
        <WorldLore
          onOpenFaction={(factionId) => {
            onFactionSelect(factionId);
            onSectionChange("factions");
          }}
          onOpenHistory={() => onSectionChange("history")}
          onOpenLocation={(locationId) => {
            onLocationSelect(locationId);
            onSectionChange("locations");
          }}
          world={world}
        />
      ) : (
        <div className="atlas-view">
          <header className="atlas-heading">
            <div>
              <span className="eyebrow">Browse the world</span>
              <h1 id="main-view-heading" tabIndex={-1}>Location atlas</h1>
            </div>
            <p>Select a place to see what is known about it.</p>
          </header>
          <div className="atlas-grid">
            <LocationBrowser
              currentLocationId={currentLocation.id}
              locations={filteredLocations}
              onQueryChange={onQueryChange}
              onSelect={onLocationSelect}
              query={query}
              selectedLocationId={selectedLocation.id}
            />
            <LocationWorkspace
              location={selectedLocation}
              onSectionChange={onLocationSectionChange}
              perspective={perspective}
              section={locationSection}
            />
          </div>
        </div>
      )}
    </div>
  );
}
