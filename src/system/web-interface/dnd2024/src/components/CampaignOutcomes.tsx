"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignOutcomes } from "../state.js";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";

export function CampaignOutcomes({
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
  const [status, setStatus] = useState("all");
  const statuses = useMemo(() => [...new Set(campaign.outcomes.map((outcome) => outcome.status))].sort(), [campaign.outcomes]);
  const outcomes = useMemo(() => filterCampaignOutcomes(campaign.outcomes, { query, status }), [campaign.outcomes, query, status]);

  return (
    <div className="campaign-section-view">
      <header className="atlas-heading"><div><span className="eyebrow">Cause and consequence</span><h1 id="main-view-heading" tabIndex={-1}>Situation outcomes</h1></div><p>{outcomes.length} of {campaign.outcomes.length} outcomes</p></header>
      <p className="campaign-section-introduction">A readable account of how important situations ended—or why they are still changing.</p>
      {campaign.outcomes.length ? <div className="campaign-controls campaign-controls--two">
        <label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search campaign outcomes</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search outcomes and consequences…" type="search" value={query} /></label>
        <label><span>Status</span><select onChange={(event) => setStatus(event.target.value)} value={status}><option value="all">All statuses</option>{statuses.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
      </div> : null}
      {outcomes.length ? (
        <section aria-label="Campaign outcomes" className="campaign-outcome-grid">
          {outcomes.map((outcome) => (
            <article className="campaign-outcome-card" key={outcome.id}>
              <header><span>{outcome.status}</span><h2>{outcome.title}</h2></header>
              <div><small>Situation</small><p>{outcome.situation}</p></div>
              <div><small>Result</small><p>{outcome.result}</p></div>
              <div className="campaign-outcome-card__consequence"><small>What changed</small><p>{outcome.consequence}</p></div>
              <CampaignEntityLinks
                links={outcome.links}
                onOpenFaction={onOpenFaction}
                onOpenLocation={onOpenLocation}
                onOpenPerson={onOpenPerson}
              />
              {outcome.dmRamification ? <aside className="campaign-dm-context"><span>DM ramification</span><p>{outcome.dmRamification}</p></aside> : null}
            </article>
          ))}
        </section>
      ) : <CampaignEmptyState
        description={campaign.outcomes.length
          ? "Try a broader status or search."
          : "Resolved or abandoned campaign arcs will appear here when they have an authored closing summary."}
        icon="ScrollText"
        title={campaign.outcomes.length ? "No outcomes match" : "No campaign outcomes recorded yet"}
      />}
    </div>
  );
}
