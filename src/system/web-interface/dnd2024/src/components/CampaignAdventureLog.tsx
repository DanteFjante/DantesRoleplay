"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignLog } from "../state.js";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { CampaignEntityLinks } from "./CampaignEntityLinks";
import { Icon } from "./Icon";

export function CampaignAdventureLog({
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
  const [order, setOrder] = useState("newest");
  const entries = useMemo(
    () => filterCampaignLog(campaign.adventureLog, { query, order }),
    [campaign.adventureLog, query, order],
  );

  return (
    <div className="campaign-section-view">
      <header className="atlas-heading">
        <div><span className="eyebrow">The remembered thread</span><h1 id="main-view-heading" tabIndex={-1}>Adventure log</h1></div>
        <p>{entries.length} of {campaign.adventureLog.length} entries</p>
      </header>
      <p className="campaign-section-introduction">Session-sized memories of what the party experienced and what those moments changed.</p>
      {campaign.adventureLog.length ? <div className="campaign-controls campaign-controls--two">
        <label className="campaign-search">
          <Icon name="Search" size={16} />
          <span className="sr-only">Search adventure log</span>
          <input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search sessions, people, places…" type="search" value={query} />
        </label>
        <label><span>Order</span><select onChange={(event) => setOrder(event.target.value)} value={order}><option value="newest">Newest first</option><option value="oldest">Oldest first</option></select></label>
      </div> : null}
      {entries.length ? (
        <ol className="campaign-log-list">
          {entries.map((entry) => (
            <li className="campaign-log-entry" key={entry.id}>
              <div className="campaign-log-entry__index">{entry.session.replace("Session ", "")}</div>
              <article>
                <header><div><span>{entry.session} · {entry.date}</span><h2>{entry.title}</h2></div></header>
                <p>{entry.summary}</p>
                <div className="campaign-result"><strong>Result</strong><p>{entry.result}</p></div>
                <CampaignEntityLinks
                  links={entry.links}
                  onOpenFaction={onOpenFaction}
                  onOpenLocation={onOpenLocation}
                  onOpenPerson={onOpenPerson}
                />
                {entry.dmNote || entry.dmThread ? (
                  <aside className="campaign-dm-context"><span>DM notes</span>{entry.dmNote ? <p><strong>Behind the scene</strong>{entry.dmNote}</p> : null}{entry.dmThread ? <p><strong>Open thread</strong>{entry.dmThread}</p> : null}</aside>
                ) : null}
              </article>
            </li>
          ))}
        </ol>
      ) : <CampaignEmptyState
        description={campaign.adventureLog.length
          ? "Try a different person, place, chapter, or session."
          : "Completed chapters and retained session recaps will appear here when the live campaign records them."}
        icon="Clock3"
        title={campaign.adventureLog.length ? "No memories match" : "No completed campaign memories yet"}
      />}
    </div>
  );
}
