"use client";

import { useMemo, useState } from "react";

import type { WorldLoreEntry, WorldReadModel } from "../data/hub-types";
import { filterWorldLore } from "../state.js";
import { Icon } from "./Icon";
import { WorldDirectoryControls } from "./WorldDirectoryControls";

function LoreLinks({
  entry,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  entry: WorldLoreEntry;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  const hasLinks =
    entry.linkedLocations.length ||
    entry.linkedPeople.length ||
    entry.linkedFactions.length ||
    entry.linkedHistory.length;
  if (!hasLinks) return null;

  return (
    <footer className="lore-card__links">
      {entry.linkedLocations.map((location) => (
        <button key={location.id} onClick={() => onOpenLocation(location.id)} type="button">
          <Icon name="MapPin" size={13} />{location.name}
        </button>
      ))}
      {entry.linkedFactions.map((faction) => (
        <button key={faction.id} onClick={() => onOpenFaction(faction.id)} type="button">
          <Icon name="Shield" size={13} />{faction.name}
        </button>
      ))}
      {entry.linkedHistory.map((event) => (
        <button key={event.id} onClick={onOpenHistory} type="button">
          <Icon name="Clock3" size={13} />{event.title}
        </button>
      ))}
      {entry.linkedPeople.map((person) => (
        <span key={person.id}><Icon name="CircleUserRound" size={13} />{person.name}<small>{person.kind}</small></span>
      ))}
    </footer>
  );
}

function LoreCard({
  entry,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  entry: WorldLoreEntry;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  return (
    <article className="lore-card" aria-labelledby={`${entry.id}-heading`}>
      <header>
        <span aria-hidden="true"><Icon name="BookOpen" size={20} /></span>
        <div>
          <small>{entry.category} · {entry.status}</small>
          <h2 id={`${entry.id}-heading`}>{entry.title}</h2>
        </div>
      </header>
      <p className="lore-card__summary">{entry.summary}</p>
      <p className="lore-card__body">{entry.body}</p>
      <LoreLinks
        entry={entry}
        onOpenFaction={onOpenFaction}
        onOpenHistory={onOpenHistory}
        onOpenLocation={onOpenLocation}
      />
      {entry.dmTruth || entry.dmNote ? (
        <aside className="directory-dm-context" aria-label={`DM context for ${entry.title}`}>
          <span><Icon name="Shield" size={15} /> DM context</span>
          {entry.dmTruth ? <p><strong>Hidden truth</strong>{entry.dmTruth}</p> : null}
          {entry.dmNote ? <p><strong>Use at table</strong>{entry.dmNote}</p> : null}
        </aside>
      ) : null}
    </article>
  );
}

export function WorldLore({
  world,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  world: WorldReadModel;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState("all");
  const [status, setStatus] = useState("all");
  const categories = useMemo(
    () => [...new Set(world.lore.map((entry) => entry.category))].sort(),
    [world.lore],
  );
  const statuses = useMemo(
    () => [...new Set(world.lore.map((entry) => entry.status))].sort(),
    [world.lore],
  );
  const entries = useMemo(
    () => filterWorldLore(world.lore, { query, category, status }),
    [world.lore, query, category, status],
  );

  return (
    <div className="world-directory-view">
      <header className="atlas-heading">
        <div>
          <span className="eyebrow">An encyclopedia of {world.name}</span>
          <h1 id="main-view-heading" tabIndex={-1}>Lore</h1>
        </div>
        <p>{entries.length} of {world.lore.length} visible</p>
      </header>
      <p className="world-directory-introduction">
        Customs, relics, places, rumours, and established truths available in this perspective.
      </p>
      <WorldDirectoryControls
        filters={[
          {
            label: "Category",
            value: category,
            onChange: setCategory,
            options: [
              { value: "all", label: "All categories" },
              ...categories.map((value) => ({ value, label: value })),
            ],
          },
          {
            label: "Status",
            value: status,
            onChange: setStatus,
            options: [
              { value: "all", label: "All statuses" },
              ...statuses.map((value) => ({ value, label: value })),
            ],
          },
        ]}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        placeholder="Search customs, relics, places, or people"
        query={query}
        searchLabel="Search world lore"
      />
      {entries.length ? (
        <div className="lore-grid">
          {entries.map((entry) => (
            <LoreCard
              entry={entry}
              key={entry.id}
              onOpenFaction={onOpenFaction}
              onOpenHistory={onOpenHistory}
              onOpenLocation={onOpenLocation}
            />
          ))}
        </div>
      ) : (
        <div className="directory-empty">
          <Icon name="BookOpen" size={26} />
          <strong>No lore matches</strong>
          <p>Try another phrase, category, or status.</p>
        </div>
      )}
    </div>
  );
}
