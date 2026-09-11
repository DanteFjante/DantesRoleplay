"use client";

import { useMemo, useState } from "react";

import type { WorldPersonDirectoryEntry, WorldReadModel } from "../data/hub-types";
import { filterWorldPeople } from "../state.js";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";
import { WorldDirectoryControls } from "./WorldDirectoryControls";

function WorldPersonDetail({ person, onOpenLocation }: {
  person: WorldPersonDirectoryEntry;
  onOpenLocation: (locationId: string) => void;
}) {
  return (
    <article aria-labelledby={`${person.id}-directory-heading`}
      className="world-person-card world-person-detail" id={`world-person-${person.id}`} tabIndex={-1}>
      <header>
        <span className="world-person-card__portrait">
          <MediaImage fallback={<span aria-hidden="true">{person.initials}</span>} media={person.portrait} />
        </span>
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
      {person.unavailableFields?.includes("motive") && !person.motive ? (
        <p><strong>Motive</strong> Unavailable for this read.</p>
      ) : null}
      {person.unavailableFields?.length ? (
        <p className="directory-completeness-notice" role="status">
          Some details for this record are unavailable: {person.unavailableFields.join(", ")}.
        </p>
      ) : null}
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

export function WorldPeopleDirectory({ world, selectedPersonId, onPersonSelect, onOpenLocation }: {
  world: WorldReadModel;
  selectedPersonId: string;
  onPersonSelect: (personId: string) => void;
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
  const selectedPerson = people.find((person) => person.id === selectedPersonId) ?? people[0] ?? null;

  return (
    <div className="world-directory-view">
      <header className="atlas-heading">
        <div>
          <span className="eyebrow">Faces across the world</span>
          <h1 id="main-view-heading" tabIndex={-1}>People &amp; creatures</h1>
        </div>
        <p>{world.peopleDirectory?.coverage === "partial"
          ? "Coverage unavailable"
          : `${people.length} of ${world.people.length} visible`}</p>
      </header>
      <p className="world-directory-introduction">
        Known people and observed creatures, gathered from the locations available in this view.
      </p>
      {world.peopleDirectory?.coverage === "partial" ? (
        <p className="directory-completeness-notice" role="status">
          Some people records are unavailable for this read. Omitted people are not confirmed absent.
        </p>
      ) : null}
      {world.peopleDirectory?.hierarchyComplete === false ? (
        <p className="directory-completeness-notice" role="status">
          This directory reached the world hierarchy safety limit. The records shown are complete
          for the loaded hierarchy, but deeper places may contain more people or creatures.
        </p>
      ) : null}
      <WorldDirectoryControls
        filters={[
          { label: "Kind", value: kind, onChange: setKind, options: [
            { value: "all", label: "All kinds" }, { value: "NPC", label: "People" },
            { value: "Creature", label: "Creatures" },
          ] },
          { label: "Region", value: region, onChange: setRegion, options: [
            { value: "all", label: "All regions" },
            ...regions.map((value) => ({ value, label: value })),
          ] },
        ]}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        placeholder="Search names, roles, places, or backgrounds" query={query}
        searchLabel="Search people and creatures"
      />
      {selectedPerson ? (
        <div className="world-person-workspace">
          <nav aria-label="People and creatures" className="world-person-list">
            {people.map((person) => (
              <button aria-pressed={selectedPerson.id === person.id} className="world-person-list__item"
                data-record-id={person.id} data-selected={selectedPerson.id === person.id ? "true" : undefined} key={person.id}
                onClick={() => onPersonSelect(person.id)} type="button">
                <span className="world-person-card__portrait">
                  <MediaImage fallback={<span aria-hidden="true">{person.initials}</span>} media={person.portrait} />
                </span>
                <span><strong>{person.name}</strong><small>{person.kind} · {person.location.name}</small></span>
                <Icon name="ChevronRight" size={16} />
              </button>
            ))}
          </nav>
          <WorldPersonDetail onOpenLocation={onOpenLocation} person={selectedPerson} />
        </div>
      ) : (
        <div className="directory-empty">
          <Icon name="UsersRound" size={26} />
          <strong>{world.peopleDirectory?.coverage === "partial"
            ? "People records unavailable"
            : "No people or creatures match"}</strong>
          <p>{world.peopleDirectory?.coverage === "partial"
            ? "Try again after the directory read recovers."
            : "Try another name, kind, or region."}</p>
        </div>
      )}
    </div>
  );
}
