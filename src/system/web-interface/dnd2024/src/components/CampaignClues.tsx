"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignClues } from "../data/campaign-filters";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";
import { KnowledgeAdmissions } from "./KnowledgeAdmissions";

export function CampaignClues({
  campaign,
  onOpenFaction,
  onOpenLocation,
  onOpenPerson,
}: {
  campaign: CampaignReadModel;
  onOpenFaction: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
  onOpenPerson: (personId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [mystery, setMystery] = useState("all");
  const [status, setStatus] = useState("all");
  const mysteries = useMemo(() => [...new Set(campaign.clues.map((clue) => clue.mystery))].sort(), [campaign.clues]);
  const statuses = useMemo(() => [...new Set(campaign.clues.map((clue) => clue.status))].sort(), [campaign.clues]);
  const clues = useMemo(() => filterCampaignClues(campaign.clues, { query, mystery, status }), [campaign.clues, mystery, query, status]);
  const unavailable = campaign.cluesCoverage === "unavailable";
  const partial = campaign.cluesCoverage === "partial";

  if (unavailable) return <CampaignEmptyState icon="Search" title="Clues unavailable"
    description="The authorized clue records have not been loaded. This is not a confirmed empty collection." />;

  return <div className="campaign-section-view">
    <header className="atlas-heading"><div><span className="eyebrow">{campaign.knowledgeAudience === "dm" ? "All evidence in DM view" : "Knowledge shared across the party"}</span><h1 id="main-view-heading" tabIndex={-1}>Clues</h1></div><p>{unavailable ? "Clues unavailable" : `${clues.length} of ${campaign.clues.length} clues${partial ? " · partial view" : ""}`}</p></header>
    <p className="campaign-section-introduction">Recorded evidence and the party's own conclusions. The page does not make deductions on the party's behalf.</p>
    {partial ? <p role="status">Some clue records are unavailable in this read. Omitted clues are not confirmed absent.</p> : null}
    {campaign.clues.length ? <div className="campaign-controls campaign-controls--three"><label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search clues</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search clues, mysteries, people…" type="search" value={query} /></label><label><span>Mystery</span><select onChange={(event) => setMystery(event.target.value)} value={mystery}><option value="all">All mysteries</option>{mysteries.map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label><span>Status</span><select onChange={(event) => setStatus(event.target.value)} value={status}><option value="all">All statuses</option>{statuses.map((value) => <option key={value} value={value}>{value}</option>)}</select></label></div> : null}
    {clues.length ? <section aria-label="Campaign clues" className="campaign-clue-grid">{clues.map((clue) =>
      <article className="campaign-clue-card" data-record-id={clue.id} key={clue.id}>
        <header><div><small>{clue.mystery} · {clue.discoveredAt}</small><h2>{clue.title}</h2></div><span>{clue.status}</span></header>
        {clue.handout ? <figure className="campaign-clue-card__handout"><MediaImage fallback={null} media={clue.handout} /></figure> : null}
        <p>{clue.detail}</p>
        <KnowledgeAdmissions admissions={clue.admissions} />
        <div className="campaign-party-conclusion"><small>The party's conclusion</small><p>{clue.partyConclusion}</p></div>
        <CampaignEntityLinks links={clue.links} onOpenFaction={onOpenFaction} onOpenLocation={onOpenLocation} onOpenPerson={onOpenPerson} />
        {clue.dmTruth || clue.dmConnection ? <aside className="campaign-dm-context"><span>DM clue context</span>{clue.dmTruth ? <p><strong>Truth</strong>{clue.dmTruth}</p> : null}{clue.dmConnection ? <p><strong>Connection</strong>{clue.dmConnection}</p> : null}</aside> : null}
      </article>)}</section> : <CampaignEmptyState description={partial ? "Some clue records could not be displayed; this is not a confirmed empty collection." : campaign.clues.length ? "Try a broader mystery, status, or search." : "No clue records are available in this perspective yet."} icon="Search" title={partial ? "Clue records unavailable" : campaign.clues.length ? "No clues match" : "No clues available yet"} />}
  </div>;
}
