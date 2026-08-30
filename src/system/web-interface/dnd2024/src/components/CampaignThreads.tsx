"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignThreads } from "../data/campaign-filters";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";

export function CampaignThreads({ campaign, onOpenLocation }: { campaign: CampaignReadModel; onOpenLocation: (locationId: string) => void }) {
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");
  const [category, setCategory] = useState("all");
  const statuses = useMemo(() => [...new Set(campaign.threads.map((thread) => thread.status))].sort(), [campaign.threads]);
  const categories = useMemo(() => [...new Set(campaign.threads.map((thread) => thread.category))].sort(), [campaign.threads]);
  const threads = useMemo(() => filterCampaignThreads(campaign.threads, { query, status, category }), [campaign.threads, category, query, status]);

  return <div className="campaign-section-view">
    <header className="atlas-heading"><div><span className="eyebrow">What remains unresolved</span><h1 id="main-view-heading" tabIndex={-1}>Open threads</h1></div><p>{threads.length} of {campaign.threads.length} threads</p></header>
    <p className="campaign-section-introduction">Mysteries, promises, and threats that the campaign is holding open. Pressure is an authored description, not a ticking rule or prediction.</p>
    {campaign.threads.length ? <div className="campaign-controls campaign-controls--three"><label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search open threads</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search threads, places, people…" type="search" value={query} /></label><label><span>Status</span><select onChange={(event) => setStatus(event.target.value)} value={status}><option value="all">All statuses</option>{statuses.map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label><span>Type</span><select onChange={(event) => setCategory(event.target.value)} value={category}><option value="all">All types</option>{categories.map((value) => <option key={value} value={value}>{value}</option>)}</select></label></div> : null}
    {threads.length ? <section aria-label="Open campaign threads" className="campaign-thread-list">{threads.map((thread) => <article className="campaign-thread-card" key={thread.id}><header><div><small>{thread.category} · {thread.status}</small><h2>{thread.title}</h2></div><span>{thread.pressure}</span></header><p>{thread.summary}</p><footer><small>Last changed</small><p>{thread.lastChanged}</p></footer><CampaignEntityLinks links={thread.links} onOpenLocation={onOpenLocation} />{thread.dmTruth || thread.dmReveal ? <aside className="campaign-dm-context"><span>DM thread context</span>{thread.dmTruth ? <p><strong>Truth</strong>{thread.dmTruth}</p> : null}{thread.dmReveal ? <p><strong>Reveal path</strong>{thread.dmReveal}</p> : null}</aside> : null}</article>)}</section> : <CampaignEmptyState description={campaign.threads.length ? "Try a broader status, type, or search." : "Active chapter questions and campaign arc stakes will appear here when the live campaign records them."} icon="Compass" title={campaign.threads.length ? "No open threads match" : "No open campaign threads recorded yet"} />}
  </div>;
}
