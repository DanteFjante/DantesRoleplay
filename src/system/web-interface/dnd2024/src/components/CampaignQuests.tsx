"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignQuests } from "../data/campaign-filters";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";

export function CampaignQuests({
  campaign,
  onOpenLocation,
}: {
  campaign: CampaignReadModel;
  onOpenLocation: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");
  const [kind, setKind] = useState("all");
  const statuses = useMemo(() => [...new Set(campaign.quests.map((quest) => quest.status))].sort(), [campaign.quests]);
  const kinds = useMemo(() => [...new Set(campaign.quests.map((quest) => quest.kind))].sort(), [campaign.quests]);
  const quests = useMemo(
    () => filterCampaignQuests(campaign.quests, { query, status, kind }),
    [campaign.quests, kind, query, status],
  );

  return (
    <div className="campaign-section-view">
      <header className="atlas-heading"><div><span className="eyebrow">What the party is pursuing</span><h1 id="main-view-heading" tabIndex={-1}>Quests</h1></div><p>{quests.length} of {campaign.quests.length} pursuits</p></header>
      <p className="campaign-section-introduction">Authored goals, their known objectives, and the next step—without automatically completing or advancing anything.</p>
      {campaign.quests.length ? <div className="campaign-controls campaign-controls--three">
        <label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search quests</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search quests, objectives, people…" type="search" value={query} /></label>
        <label><span>Status</span><select onChange={(event) => setStatus(event.target.value)} value={status}><option value="all">All statuses</option>{statuses.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
        <label><span>Kind</span><select onChange={(event) => setKind(event.target.value)} value={kind}><option value="all">All kinds</option>{kinds.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
      </div> : null}
      {quests.length ? <section aria-label="Campaign quests" className="campaign-quest-grid">
        {quests.map((quest) => <article className="campaign-quest-card" key={quest.id}>
          <header><div><small>{quest.kind}</small><h2>{quest.title}</h2></div><span>{quest.status}</span></header>
          <p>{quest.summary}</p>
          <div className="campaign-next-step"><small>Next step</small><p>{quest.nextStep}</p></div>
          <ol className="campaign-objectives">{quest.objectives.map((objective) => <li key={objective.id}><span>{objective.status}</span><p>{objective.text}</p></li>)}</ol>
          <CampaignEntityLinks links={quest.links} onOpenLocation={onOpenLocation} />
          {quest.dmContext ? <aside className="campaign-dm-context"><span>DM context</span><p>{quest.dmContext}</p></aside> : null}
        </article>)}
      </section> : <CampaignEmptyState
        description={campaign.quests.length
          ? "Try a broader status, kind, or search."
          : "Party goals from the live campaign will appear here when they are recorded."}
        icon="ScrollText"
        title={campaign.quests.length ? "No quests match" : "No campaign goals recorded yet"}
      />}
    </div>
  );
}
