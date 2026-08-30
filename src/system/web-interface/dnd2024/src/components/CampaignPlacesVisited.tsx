"use client";

import { useMemo, useState } from "react";

import type { CampaignReadModel } from "../data/hub-types";
import { filterCampaignPlaces } from "../state.js";
import { CampaignEmptyState } from "./CampaignEmptyState";
import { Icon } from "./Icon";

export function CampaignPlacesVisited({
  campaign,
  onOpenLocation,
}: {
  campaign: CampaignReadModel;
  onOpenLocation: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("all");
  const regions = useMemo(() => [...new Set(campaign.placesVisited.map((place) => place.location.region))].sort(), [campaign.placesVisited]);
  const places = useMemo(() => filterCampaignPlaces(campaign.placesVisited, { query, region }), [campaign.placesVisited, query, region]);

  return (
    <div className="campaign-section-view">
      <header className="atlas-heading"><div><span className="eyebrow">The party's path</span><h1 id="main-view-heading" tabIndex={-1}>Places visited</h1></div><p>{places.length} of {campaign.placesVisited.length} places</p></header>
      <p className="campaign-section-introduction">Campaign memories linked back to the persistent World. Opening a card never records travel or movement.</p>
      {campaign.placesVisited.length ? <div className="campaign-controls campaign-controls--two">
        <label className="campaign-search"><Icon name="Search" size={16} /><span className="sr-only">Search visited places</span><input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder="Search places and memories…" type="search" value={query} /></label>
        <label><span>Region</span><select onChange={(event) => setRegion(event.target.value)} value={region}><option value="all">All regions</option>{regions.map((value) => <option key={value} value={value}>{value}</option>)}</select></label>
      </div> : null}
      {places.length ? (
        <section aria-label="Visited places" className="campaign-place-grid">
          {places.map((place) => (
            <article className="campaign-place-card" key={place.id}>
              <header><span className="campaign-place-card__icon"><Icon name="MapPin" /></span><div><small>{place.location.region}</small><h2>{place.location.name}</h2></div><em>{place.status}</em></header>
              <p>{place.summary}</p>
              <dl><div><dt>First visit</dt><dd>{place.firstVisited}</dd></div><div><dt>Last visit</dt><dd>{place.lastVisited}</dd></div><div><dt>Visits</dt><dd>{place.visitCount}</dd></div></dl>
              <blockquote>{place.memory}</blockquote>
              {place.dmContext ? <aside className="campaign-dm-context"><span>DM context</span><p>{place.dmContext}</p></aside> : null}
              <button className="text-action" onClick={() => onOpenLocation(place.location.id)} type="button">Open World location <Icon name="ArrowRight" size={15} /></button>
            </article>
          ))}
        </section>
      ) : <CampaignEmptyState
        description={campaign.placesVisited.length
          ? "Clear the filters to see the whole campaign trail."
          : "This page waits for explicit campaign visit records; it never guesses visits from the current location or the map."}
        icon="MapPin"
        title={campaign.placesVisited.length ? "No visited places match" : "No campaign visits recorded yet"}
      />}
    </div>
  );
}
