"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignClues } from "../data/campaign-filters";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";

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

  return <div className="campaign-section-view">
    <header className="atlas-heading"><div><span className="eyebrow">What the party actually knows</span><h1 id="main-view-heading" tabIndex={-1}>Clues</h1></div><p>{clues.length} of {campaign.clues.length} clues</p></header>
    <p className="campaign-section-introduction">Recorded evidence and the party's own conclusions. The page does not make deductions on the party's behalf.</p>
    {campaign.clues.length ? <div className="campaign-controls campaign-controls--three"><label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search clues</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search clues, mysteries, people…" type="search" value={query} /></label><label><span>Mystery</span><select onChange={(event) => setMystery(event.target.value)} value={mystery}><option value="all">All mysteries</option>{mysteries.map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label><span>Status</span><select onChange={(event) => setStatus(event.target.value)} value={status}><option value="all">All statuses</option>{statuses.map((value) => <option key={value} value={value}>{value}</option>)}</select></label></div> : null}
    {clues.length ? <section aria-label="Campaign clues" className="campaign-clue-grid">{clues.map((clue) => <article className="campaign-clue-card" key={clue.id}><header><div><small>{clue.mystery} · {clue.discoveredAt}</small><h2>{clue.title}</h2></div><span>{clue.status}</span></header><p>{clue.detail}</p><div className="campaign-party-conclusion"><small>The party's conclusion</small><p>{clue.partyConclusion}</p></div><CampaignEntityLinks links={clue.links} onOpenFaction={onOpenFaction} onOpenLocation={onOpenLocation} onOpenPerson={onOpenPerson} />{clue.dmTruth || clue.dmConnection ? <aside className="campaign-dm-context"><span>DM clue context</span>{clue.dmTruth ? <p><strong>Truth</strong>{clue.dmTruth}</p> : null}{clue.dmConnection ? <p><strong>Connection</strong>{clue.dmConnection}</p> : null}</aside> : null}</article>)}</section> : <CampaignEmptyState description={campaign.clues.length ? "Try a broader mystery, status, or search." : "The live game has not recorded campaign-owned clue entries yet. World knowledge remains available in the World tab."} icon="Search" title={campaign.clues.length ? "No clues match" : "No campaign clues recorded yet"} />}
  </div>;
}
