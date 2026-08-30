import type { CampaignSectionId } from "../data/hub-types";
import { CAMPAIGN_SECTIONS } from "../state.js";

export function CampaignSectionNavigation({
  activeSection,
  onSelect,
}: {
  activeSection: CampaignSectionId;
  onSelect: (section: CampaignSectionId) => void;
}) {
  return (
    <nav aria-label="Campaign sections" className="section-tabs">
      {CAMPAIGN_SECTIONS.map((section) => (
        <button
          aria-current={activeSection === section.id ? "page" : undefined}
          key={section.id}
          onClick={() => onSelect(section.id as CampaignSectionId)}
          type="button"
        >
          {section.label}
        </button>
      ))}
    </nav>
  );
}
