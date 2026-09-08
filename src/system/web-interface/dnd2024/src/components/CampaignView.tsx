import type { CampaignReadModel, CampaignSectionId } from "../data/hub-types";
import type { ResourceEdit } from "../data/resource-state";
import { CampaignAdventureLog } from "./CampaignAdventureLog";
import { CampaignClues } from "./CampaignClues";
import { CampaignOutcomes } from "./CampaignOutcomes";
import { CampaignOverview } from "./CampaignOverview";
import { CampaignPlacesVisited } from "./CampaignPlacesVisited";
import { CampaignQuests } from "./CampaignQuests";
import { CampaignSectionNavigation } from "./CampaignSectionNavigation";
import { CampaignThreads } from "./CampaignThreads";

export function CampaignView({
  campaign,
  premiseEdit,
  section,
  worldName,
  onOpenFaction,
  onOpenLocation,
  onOpenPerson,
  onBeginPremiseEdit,
  onCancelPremiseEdit,
  onPremiseDraftChange,
  onSavePremise,
  onSectionChange,
}: {
  campaign: CampaignReadModel;
  premiseEdit?: ResourceEdit;
  section: CampaignSectionId;
  worldName: string;
  onOpenFaction: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
  onOpenPerson: (personId: string) => void;
  onBeginPremiseEdit?: () => void;
  onCancelPremiseEdit?: () => void;
  onPremiseDraftChange?: (premise: string) => void;
  onSavePremise?: (premise: string) => void;
  onSectionChange: (section: CampaignSectionId) => void;
}) {
  return (
    <div className="campaign-view">
      <CampaignSectionNavigation activeSection={section} onSelect={onSectionChange} />
      {section === "log" ? (
        <CampaignAdventureLog campaign={campaign} onOpenFaction={onOpenFaction} onOpenLocation={onOpenLocation} onOpenPerson={onOpenPerson} />
      ) : section === "places" ? (
        <CampaignPlacesVisited campaign={campaign} onOpenLocation={onOpenLocation} />
      ) : section === "outcomes" ? (
        <CampaignOutcomes campaign={campaign} onOpenFaction={onOpenFaction} onOpenLocation={onOpenLocation} onOpenPerson={onOpenPerson} />
      ) : section === "quests" ? (
        <CampaignQuests campaign={campaign} onOpenLocation={onOpenLocation} />
      ) : section === "threads" ? (
        <CampaignThreads campaign={campaign} onOpenLocation={onOpenLocation} />
      ) : section === "clues" ? (
        <CampaignClues campaign={campaign} onOpenFaction={onOpenFaction} onOpenLocation={onOpenLocation} onOpenPerson={onOpenPerson} />
      ) : (
        <CampaignOverview
          campaign={campaign}
          premiseEdit={premiseEdit}
          worldName={worldName}
          onBeginPremiseEdit={onBeginPremiseEdit}
          onCancelPremiseEdit={onCancelPremiseEdit}
          onPremiseDraftChange={onPremiseDraftChange}
          onSavePremise={onSavePremise}
          onSectionChange={onSectionChange}
        />
      )}
    </div>
  );
}
