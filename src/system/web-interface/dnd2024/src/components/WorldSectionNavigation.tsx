import type { WorldSectionId } from "../data/hub-types";
import { WORLD_SECTIONS } from "../state.js";

export function WorldSectionNavigation({
  activeSection,
  onSelect,
}: {
  activeSection: WorldSectionId;
  onSelect: (section: WorldSectionId) => void;
}) {
  return (
    <nav aria-label="World sections" className="section-tabs">
      {WORLD_SECTIONS.map((section) => (
        <button
          aria-current={activeSection === section.id ? "page" : undefined}
          key={section.id}
          onClick={() => onSelect(section.id as WorldSectionId)}
          type="button"
        >
          {section.label}
        </button>
      ))}
    </nav>
  );
}
