"use client";

import { useMemo, useState } from "react";

import type { WorldPersonDirectoryEntry, WorldReadModel } from "../data/hub-types";
import { filterWorldPeople } from "../state.js";
import { Icon } from "./Icon";
import { WorldDirectoryControls } from "./WorldDirectoryControls";

function WorldPersonCard({
  person,
  selected,
  onOpenLocation,
}: {
  person: WorldPersonDirectoryEntry;
  selected: boolean;
  onOpenLocation: (locationId: string) => void;
}) {
  return (
    <article
      aria-labelledby={`${person.id}-directory-heading`}
      className="world-person-card"
      data-selected={selected ? "true" : undefined}
      id={`world-person-${person.id}`}
      tabIndex={-1}
    >
      <header>
        <span className="world-person-card__portrait" aria-hidden="true">{person.initials}</span>
        <div>
          <small>{person.kind} · {person.role}</small>
          <h2 id={`${person.id}-directory-heading`}>{person.name}</h2>
          <p>{person.disposition}</p>
        </div>
      </header>
      <p className="world-person-card__summary">{person.summary}</p>
      <div className="world-person-card__background">
        <strong>Background</strong>
        <p>{person.background}</p>
      </div>
      <button className="directory-link-button" onClick={() => onOpenLocation(person.location.id)} type="button">
        <Icon name="MapPin" size={14} />
        <span>{person.location.name}<small>{person.location.region}</small></span>
        <Icon name="ArrowRight" size={14} />
      </button>
      {person.motive || person.dmSecret ? (
        <aside className="directory-dm-context" aria-label={`DM context for ${person.name}`}>
          <span><Icon name="Shield" size={15} /> DM context</span>
          {person.motive ? <p><strong>Motive</strong>{person.motive}</p> : null}
          {person.dmSecret ? <p><strong>Secret</strong>{person.dmSecret}</p> : null}
        </aside>
      ) : null}
    </article>
  );
}

export function WorldPeopleDirectory({
  world,
  selectedPersonId,
  onOpenLocation,
}: {
  world: WorldReadModel;
  selectedPersonId: string;
  onOpenLocation: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [region, setRegion] = useState("all");
  const regions = useMemo(
    () => [...new Set(world.people.map((person) => person.location.region))].sort(),
    [world.people],
  );
  const people = useMemo(
    () => filterWorldPeople(world.people, { query, kind, region }),
    [world.people, query, kind, region],
  );

  return (
    <div className="world-directory-view">
      <header className="atlas-heading">
        <div>
          <span className="eyebrow">Faces across the world</span>
          <h1 id="main-view-heading" tabIndex={-1}>People &amp; creatures</h1>
        </div>
        <p>{people.length} of {world.people.length} visible</p>
      </header>
      <p className="world-directory-introduction">
        Known people and observed creatures, gathered from the locations available in this view.
      </p>
      <WorldDirectoryControls
        filters={[
          {
            label: "Kind",
            value: kind,
            onChange: setKind,
            options: [
              { value: "all", label: "All kinds" },
              { value: "NPC", label: "People" },
              { value: "Creature", label: "Creatures" },
            ],
          },
          {
            label: "Region",
            value: region,
            onChange: setRegion,
            options: [
              { value: "all", label: "All regions" },
              ...regions.map((value) => ({ value, label: value })),
            ],
          },
        ]}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        placeholder="Search names, roles, places, or backgrounds"
        query={query}
        searchLabel="Search people and creatures"
      />
      {people.length ? (
        <div className="world-person-grid">
          {people.map((person) => (
            <WorldPersonCard
              key={person.id}
              onOpenLocation={onOpenLocation}
              person={person}
              selected={selectedPersonId === person.id}
            />
          ))}
        </div>
      ) : (
        <div className="directory-empty">
          <Icon name="UsersRound" size={26} />
          <strong>No people or creatures match</strong>
          <p>Try another name, kind, or region.</p>
        </div>
      )}
    </div>
  );
}
