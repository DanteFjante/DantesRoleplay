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
  detailsError,
  detailsStatus,
  hasValidatedDetails,
  premiseEdit,
  section,
  worldName,
  onOpenFaction,
  onOpenLocation,
  onOpenPerson,
  onBeginPremiseEdit,
  onCancelPremiseEdit,
  onPremiseDraftChange,
  onRetryDetails,
  onSavePremise,
  onSectionChange,
}: {
  campaign: CampaignReadModel;
  detailsError: string;
  detailsStatus: "unloaded" | "loading" | "ready" | "error";
  hasValidatedDetails: boolean;
  premiseEdit?: ResourceEdit;
  section: CampaignSectionId;
  worldName: string;
  onOpenFaction: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
  onOpenPerson: (personId: string) => void;
  onBeginPremiseEdit?: () => void;
  onCancelPremiseEdit?: () => void;
  onPremiseDraftChange?: (premise: string) => void;
  onRetryDetails?: () => void;
  onSavePremise?: (premise: string) => void;
  onSectionChange: (section: CampaignSectionId) => void;
}) {
  const detailNotice = detailsStatus === "ready" ? null : (
    <section
      aria-busy={detailsStatus === "loading" || detailsStatus === "unloaded"}
      className={`campaign-detail-status campaign-detail-status--${detailsStatus}`}
      role={detailsStatus === "error" ? "alert" : "status"}
    >
      <div>
        <strong>{detailsStatus === "error"
          ? hasValidatedDetails ? "Latest campaign details unavailable" : "Campaign details unavailable"
          : hasValidatedDetails ? "Refreshing campaign details" : "Loading campaign details"}</strong>
        <p>{detailsStatus === "error"
          ? hasValidatedDetails
            ? "Showing the last validated campaign details. Try again to check for newer chapters, arcs, and visits."
            : detailsError || "The campaign details could not be loaded."
          : hasValidatedDetails
            ? "The last validated details remain available while the campaign refreshes."
            : "Loading the selected campaign's chapters, arcs, sessions, and recorded visits."}</p>
        {detailsStatus === "error" && hasValidatedDetails && detailsError
          ? <small>{detailsError}</small> : null}
      </div>
      {detailsStatus === "error" && onRetryDetails
        ? <button onClick={onRetryDetails} type="button">Retry campaign details</button> : null}
    </section>
  );
  const incompleteFields = Object.entries(campaign.detailFields ?? {})
    .filter(([, status]) => status !== "ready" && status !== "empty")
    .map(([field]) => field);
  const coverageNotice = detailsStatus === "ready" && incompleteFields.length > 0 ? (
    <section className="campaign-detail-status campaign-detail-status--partial" role="status">
      <strong>Some campaign details are unavailable</strong>
      <p>{incompleteFields.map((field) => field === "visits" ? "recorded visits" : field).join(", ")} could not be fully read. Displayed records are retained; omitted records are not confirmed absent.</p>
    </section>
  ) : null;

  return (
    <div className="campaign-view">
      <CampaignSectionNavigation activeSection={section} onSelect={onSectionChange} />
      {detailNotice}
      {coverageNotice}
      {!hasValidatedDetails && section !== "overview" ? null : section === "log" ? (
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
          detailsAvailable={hasValidatedDetails}
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
