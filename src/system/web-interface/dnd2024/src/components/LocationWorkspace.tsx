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
  location: WorldLocation | null;
  perspective: Perspective;
  section: LocationSectionId;
  onSectionChange: (section: LocationSectionId) => void;
}) {
  if (!location) return (
    <section className="location-workspace location-workspace--empty" aria-labelledby="location-selection-heading">
      <span className="eyebrow">Location details</span>
      <h2 id="location-selection-heading">Choose a location</h2>
      <p>Select a place from this level to open its details and browse what it contains.</p>
    </section>
  );
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
