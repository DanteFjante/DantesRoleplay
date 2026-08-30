import type {
  LocationSectionId,
  Perspective,
  WorldLocation,
} from "../data/hub-types";
import { LocationDetail } from "./LocationDetail";
import { LocationHoldings } from "./LocationHoldings";
import { LocationPeople } from "./LocationPeople";
import { LocationSectionNavigation } from "./LocationSectionNavigation";

export function LocationWorkspace({
  location,
  perspective,
  section,
  onSectionChange,
}: {
  location: WorldLocation;
  perspective: Perspective;
  section: LocationSectionId;
  onSectionChange: (section: LocationSectionId) => void;
}) {
  return (
    <div className="location-workspace">
      <LocationSectionNavigation
        activeSection={section}
        locationName={location.name}
        onSelect={onSectionChange}
        perspective={perspective}
      />
      {section === "people" ? (
        <LocationPeople location={location} />
      ) : section === "holdings" && perspective === "dm" ? (
        <LocationHoldings location={location} />
      ) : (
        <LocationDetail location={location} />
      )}
    </div>
  );
}
