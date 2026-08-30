import type { LocationSectionId, Perspective } from "../data/hub-types";
import { LOCATION_SECTIONS } from "../state.js";

export function LocationSectionNavigation({
  activeSection,
  locationName,
  perspective,
  onSelect,
}: {
  activeSection: LocationSectionId;
  locationName: string;
  perspective: Perspective;
  onSelect: (section: LocationSectionId) => void;
}) {
  const sections = LOCATION_SECTIONS.filter(
    (section) => !section.dmOnly || perspective === "dm",
  );

  return (
    <nav aria-label={`${locationName} information`} className="location-section-tabs">
      {sections.map((section) => (
        <button
          aria-current={activeSection === section.id ? "page" : undefined}
          key={section.id}
          onClick={() => onSelect(section.id as LocationSectionId)}
          type="button"
        >
          {section.label}
          {section.dmOnly ? <span>DM</span> : null}
        </button>
      ))}
    </nav>
  );
}
