"use client";

import { useEffect, useMemo, useState } from "react";

import type { WorldFaction, WorldReadModel } from "../data/hub-types";
import { filterWorldFactions } from "../state.js";
import { selectWorldFactions } from "../data/presentation-records";
import { Icon } from "./Icon";
import { WorldDirectoryControls } from "./WorldDirectoryControls";

function FactionCard({
  faction,
  selected,
  onSelect,
  onOpenLocation,
}: {
  faction: WorldFaction;
  selected: boolean;
  onSelect: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
}) {
  return (
    <article
      className="faction-card"
      data-record-id={faction.id}
      data-selected={selected ? "true" : undefined}
      aria-labelledby={`${faction.id}-heading`}
      id={`world-faction-${faction.id}`}
      onFocus={() => onSelect(faction.id)}
      tabIndex={-1}
    >
      <header>
        <button
          aria-label={`Select ${faction.name}`}
          className="faction-card__mark"
          onClick={() => onSelect(faction.id)}
          type="button"
        >
          {faction.monogram}
        </button>
        <div>
          <small>{faction.kind ?? "Organization"} · {faction.influence} · {faction.status}</small>
          <h2 id={`${faction.id}-heading`}>{faction.name}</h2>
        </div>
      </header>
      <p className="faction-card__summary">{faction.summary}</p>
      {faction.unavailableFields?.length ? (
        <p className="directory-record-notice" role="status">
          Some details are unavailable: {faction.unavailableFields.join(", ")}.
        </p>
      ) : null}
      {faction.goals.length || faction.methods.length ? (
        <div className="faction-card__columns">
          {faction.goals.length ? (
            <section>
              <h3>Goals</h3>
              <ul>{faction.goals.map((goal) => <li key={goal}>{goal}</li>)}</ul>
            </section>
          ) : null}
          {faction.methods.length ? (
            <section>
              <h3>Methods</h3>
              <ul>{faction.methods.map((method) => <li key={method}>{method}</li>)}</ul>
            </section>
          ) : null}
        </div>
      ) : null}
      {(faction.assets ?? []).length ? (
        <section className="faction-card__references">
          <h3><Icon name="PackageOpen" size={14} /> Assets</h3>
          <ul>{(faction.assets ?? []).map((asset) => <li key={asset}>{asset}</li>)}</ul>
        </section>
      ) : null}
      {faction.members.length ? (
        <section className="faction-card__references">
          <h3><Icon name="UsersRound" size={14} /> Known members</h3>
          <div>{faction.members.map((member) => <span key={member.id}>{member.name}<small>{member.kind}</small></span>)}</div>
        </section>
      ) : null}
      {faction.territories.length ? (
        <section className="faction-card__references">
          <h3><Icon name="MapPin" size={14} /> Presence</h3>
          <div>
            {faction.territories.map((territory) => (
              <button key={territory.id} onClick={() => onOpenLocation(territory.id)} type="button">
                {territory.name}<Icon name="ArrowRight" size={13} />
              </button>
            ))}
          </div>
        </section>
      ) : null}
      {faction.relationships.length ? (
        <section className="faction-card__references">
          <h3><Icon name="Route" size={14} /> Relationships</h3>
          <div>
            {faction.relationships.map((relationship) => (
              <button key={`${relationship.id}-${relationship.stance}`} onClick={() => onSelect(relationship.id)} type="button">
                {relationship.name}<small>{relationship.stance}</small>
              </button>
            ))}
          </div>
        </section>
      ) : null}
      {faction.dmAgenda || faction.dmSecret ? (
        <aside className="directory-dm-context" aria-label={`DM context for ${faction.name}`}>
          <span><Icon name="Shield" size={15} /> DM context</span>
          {faction.dmAgenda ? <p><strong>Agenda</strong>{faction.dmAgenda}</p> : null}
          {faction.dmSecret ? <p><strong>Secret</strong>{faction.dmSecret}</p> : null}
        </aside>
      ) : null}
    </article>
  );
}

export function WorldFactions({
  world,
  selectedFactionId,
  onFactionSelect,
  onOpenLocation,
  busy,
  onLoadMore,
}: {
  world: WorldReadModel;
  selectedFactionId: string;
  onFactionSelect: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
  busy?: boolean;
  onLoadMore?: () => void;
}) {
  const [query, setQuery] = useState("");
  const [influence, setInfluence] = useState("all");
  const selected = useMemo(() => selectWorldFactions((world as { factions?: unknown }).factions), [world.factions]);
  useEffect(() => { setQuery(""); setInfluence("all"); }, [world.id]);
  const influences = useMemo(
    () => [...new Set(selected.records.map((faction) => faction.influence))].sort(),
    [selected.records],
  );
  const factions = useMemo(
    () => filterWorldFactions(selected.records, { query, influence }),
    [selected.records, query, influence],
  );
  const sourceTotal = world.factionDirectory?.totalCount ?? selected.records.length + selected.omittedCount;
  const completePage = world.factionDirectory?.complete ?? true;
  const omittedCompleteRows = completePage && sourceTotal > selected.records.length;
  const partialCoverage = world.factionDirectory?.coverage === "partial" || selected.omittedCount > 0 || omittedCompleteRows || selected.records.some(
    (faction) => (faction.unavailableFields?.length ?? 0) > 0,
  );

  return (
    <div className="world-directory-view">
      <header className="atlas-heading">
        <div>
          <span className="eyebrow">Powers in motion</span>
          <h1 id="main-view-heading" tabIndex={-1}>Factions</h1>
        </div>
        <p aria-live="polite">{factions.length} visible · {selected.records.length} displayed from {sourceTotal} source records</p>
      </header>
      <p className="world-directory-introduction">
        Sovereign powers and organizations whose rule, goals, alliances, and rivalries continue to shape {world.name}.
      </p>
      <WorldDirectoryControls
        filters={[
          {
            label: "Influence",
            value: influence,
            onChange: setInfluence,
            options: [
              { value: "all", label: "All influence" },
              ...influences.map((value) => ({ value, label: value })),
            ],
          },
        ]}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        placeholder="Search factions, goals, members, or places"
        query={query}
        searchLabel="Search factions"
      />
      {partialCoverage ? (
        <p className="directory-record-notice" role="status">
          Some faction records or details are unavailable. Omitted records are not confirmed absent.
        </p>
      ) : null}
      {factions.length ? (
        <div className="faction-grid">
          {factions.map((faction) => (
            <FactionCard
              faction={faction}
              key={faction.id}
              onOpenLocation={onOpenLocation}
              onSelect={onFactionSelect}
              selected={selectedFactionId === faction.id}
            />
          ))}
        </div>
      ) : (
        <div className="directory-empty">
          <Icon name="Shield" size={26} />
          <strong>{partialCoverage ? "Faction records unavailable" : "No factions match"}</strong>
          <p>{partialCoverage
            ? "The source reported records that could not be displayed."
            : "Try another name or influence level."}</p>
        </div>
      )}
      {world.factionDirectory && !world.factionDirectory.complete ? (
        <div className="directory-pagination">
          <button aria-busy={busy ? "true" : undefined} disabled={busy} onClick={onLoadMore} type="button">
            {busy ? "Loading factions…" : "Load more factions"}
          </button>
          <p>Additional faction pages may remain.</p>
        </div>
      ) : null}
    </div>
  );
}
