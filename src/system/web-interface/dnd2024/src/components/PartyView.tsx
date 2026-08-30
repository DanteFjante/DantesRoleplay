"use client";

import { useEffect, useMemo, useState } from "react";

import type {
  PartyDossierEntry,
  PartyKnowledgeEntry,
  PartyMemberReadModel,
  PartySectionId,
} from "../data/hub-types";
import { Icon } from "./Icon";

const PARTY_SECTIONS: ReadonlyArray<{ id: PartySectionId; label: string; icon: string }> = [
  { id: "overview", label: "Overview", icon: "CircleUserRound" },
  { id: "sheet", label: "Sheet", icon: "Shield" },
  { id: "knowledge", label: "Knowledge", icon: "BookOpen" },
  { id: "backstory", label: "Backstory", icon: "ScrollText" },
  { id: "origin", label: "Origin", icon: "Sparkles" },
  { id: "inventory", label: "Inventory", icon: "PackageOpen" },
];

function EmptySection({ title, copy }: { title: string; copy: string }) {
  return (
    <section className="party-empty-state">
      <span><Icon name="ScrollText" size={24} /></span>
      <div>
        <strong>{title}</strong>
        <p>{copy}</p>
      </div>
    </section>
  );
}

function DossierEntries({ entries }: { entries: PartyDossierEntry[] }) {
  return (
    <div className="party-entry-grid">
      {entries.map((entry) => (
        <article className="party-entry-card" key={entry.id}>
          <span>{entry.kind}</span>
          <h3>{entry.title}</h3>
          <p>{entry.detail}</p>
        </article>
      ))}
    </div>
  );
}

function KnowledgeEntries({ entries }: { entries: PartyKnowledgeEntry[] }) {
  return (
    <div className="party-entry-grid">
      {entries.map((entry) => (
        <article className="party-entry-card party-entry-card--knowledge" key={entry.id}>
          <div className="party-entry-card__meta">
            <span>{entry.kind}</span>
            <small>{entry.stance}</small>
          </div>
          <p>{entry.text}</p>
        </article>
      ))}
    </div>
  );
}

function sectionEntries(member: PartyMemberReadModel, section: PartySectionId) {
  switch (section) {
    case "sheet": return member.sheet;
    case "knowledge": return member.knowledge;
    case "backstory": return member.backstory;
    case "origin": return member.origin;
    case "inventory": return member.inventory;
    default: return [];
  }
}

function sectionEmptyCopy(section: PartySectionId, member: PartyMemberReadModel) {
  switch (section) {
    case "sheet":
      return "No character-sheet entries are available yet. Mechanical values will appear only when the canonical sheet projection is connected.";
    case "knowledge":
      return member.isCurrent
        ? "No character knowledge is currently available from the authorized notebook."
        : "Character knowledge is private to an authorized player seat and is not inferred from the DM record.";
    case "backstory":
      return "No biography or backstory notes have been recorded for this character.";
    case "origin":
      return "No class direction or background has been recorded for this character.";
    case "inventory":
      return "No inventory entries are available. The site will not infer belongings from prose or equipment suggestions.";
    default:
      return "No information has been recorded for this section.";
  }
}

function Overview({ member }: { member: PartyMemberReadModel }) {
  const highlights = [...member.origin, ...member.sheet, ...member.backstory]
    .filter((entry, index, all) => all.findIndex((candidate) => candidate.id === entry.id) === index)
    .slice(0, 4);
  const availableSections = [
    member.sheet.length,
    member.knowledge.length,
    member.backstory.length,
    member.origin.length,
    member.inventory.length,
  ].filter((count) => count > 0).length;

  return (
    <div className="party-overview">
      <section className="party-overview__facts" aria-label={`${member.name} summary`}>
        <article><span>Status</span><strong>{member.status}</strong><small>Campaign participation</small></article>
        <article><span>Record</span><strong>{member.recordStatus}</strong><small>No values are inferred</small></article>
        <article><span>Dossier</span><strong>{availableSections} sections</strong><small>With recorded information</small></article>
      </section>
      {highlights.length ? (
        <section className="party-overview__highlights">
          <div className="party-section-heading">
            <div><span className="eyebrow">At a glance</span><h2>Character highlights</h2></div>
            <p>{highlights.length} recorded details</p>
          </div>
          <DossierEntries entries={highlights} />
        </section>
      ) : (
        <EmptySection
          copy="The active participant is known, but no character dossier has been recorded yet."
          title="Identity available"
        />
      )}
    </div>
  );
}

export function PartyView({ party }: { party: PartyMemberReadModel[] }) {
  const [selectedMemberId, setSelectedMemberId] = useState(party[0]?.id ?? "");
  const [section, setSection] = useState<PartySectionId>("overview");
  const [query, setQuery] = useState("");

  useEffect(() => {
    if (!party.some((member) => member.id === selectedMemberId)) {
      setSelectedMemberId(party[0]?.id ?? "");
      setSection("overview");
      setQuery("");
    }
  }, [party, selectedMemberId]);

  const selectedMember = party.find((member) => member.id === selectedMemberId) ?? party[0];
  const entries = selectedMember ? sectionEntries(selectedMember, section) : [];
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const filteredEntries = useMemo(() => entries.filter((entry) => {
    if (!normalizedQuery) return true;
    if ("text" in entry) {
      return [entry.kind, entry.stance, entry.text]
        .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    }
    return [entry.kind, entry.title, entry.detail]
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  }), [entries, normalizedQuery]);

  if (!selectedMember) {
    return (
      <div className="party-view">
        <header className="view-intro">
          <span className="eyebrow">Campaign companions</span>
          <h1 id="main-view-heading" tabIndex={-1}>The party</h1>
          <p>No Player-safe party roster is available in this perspective.</p>
        </header>
        <EmptySection
          copy="Open DM perspective to inspect the active roster, or use a bound Player seat to view its current character."
          title="Party information unavailable"
        />
      </div>
    );
  }

  return (
    <div className="party-view">
      <header className="party-heading">
        <div>
          <span className="eyebrow">Campaign companions</span>
          <h1 id="main-view-heading" tabIndex={-1}>The party</h1>
          <p>Select a character to open the records available in this perspective.</p>
        </div>
        <strong>{party.length} active {party.length === 1 ? "character" : "characters"}</strong>
      </header>

      <div className="party-workspace">
        <aside className="party-roster" aria-label="Active party roster">
          <div className="party-roster__heading">
            <span className="eyebrow">Active roster</span>
            <small>{party.length}</small>
          </div>
          {party.map((member) => (
            <button
              aria-current={member.id === selectedMember.id ? "true" : undefined}
              className="party-roster-card"
              key={member.id}
              onClick={() => {
                setSelectedMemberId(member.id);
                setSection("overview");
                setQuery("");
              }}
              type="button"
            >
              <span className="party-roster-card__portrait">{member.initials}</span>
              <span className="party-roster-card__copy">
                <small>{member.isCurrent ? "Current character" : member.recordStatus}</small>
                <strong>{member.name}</strong>
                <span>{member.detail}</span>
              </span>
              <Icon name="ChevronRight" size={17} />
            </button>
          ))}
        </aside>

        <section className="party-dossier" aria-label={`${selectedMember.name} dossier`}>
          <header className="party-dossier__hero">
            <span className="party-dossier__portrait">{selectedMember.initials}</span>
            <div>
              <span className="eyebrow">{selectedMember.isCurrent ? "Your character" : "Party character"}</span>
              <h2>{selectedMember.name}</h2>
              <p>{selectedMember.detail}</p>
            </div>
            <div className="party-dossier__status">
              <strong>{selectedMember.status}</strong>
              <span>{selectedMember.recordStatus}</span>
            </div>
          </header>

          <nav aria-label="Character dossier sections" className="party-section-nav">
            {PARTY_SECTIONS.map((candidate) => (
              <button
                aria-current={section === candidate.id ? "page" : undefined}
                key={candidate.id}
                onClick={() => {
                  setSection(candidate.id);
                  setQuery("");
                }}
                type="button"
              >
                <Icon name={candidate.icon} size={16} />
                {candidate.label}
              </button>
            ))}
          </nav>

          <div className="party-dossier__content">
            {section === "overview" ? (
              <Overview member={selectedMember} />
            ) : (
              <>
                <div className="party-section-heading">
                  <div>
                    <span className="eyebrow">{selectedMember.name}</span>
                    <h2>{PARTY_SECTIONS.find((candidate) => candidate.id === section)?.label}</h2>
                  </div>
                  <p>{entries.length} recorded {entries.length === 1 ? "entry" : "entries"}</p>
                </div>
                {section === "sheet" || section === "inventory" ? (
                  <p className="party-provisional-note">
                    <Icon name="Shield" size={16} />
                    {section === "sheet"
                      ? "These are recorded character directions, not derived mechanical sheet values."
                      : "These are recorded belongings, not a canonical custody or equipment calculation."}
                  </p>
                ) : null}
                {entries.length > 8 ? (
                  <label className="party-search">
                    <Icon name="Search" size={17} />
                    <span className="sr-only">Search {section}</span>
                    <input
                      onChange={(event) => setQuery(event.target.value.slice(0, 80))}
                      placeholder={`Search ${section}…`}
                      type="search"
                      value={query}
                    />
                  </label>
                ) : null}
                {filteredEntries.length ? (
                  section === "knowledge"
                    ? <KnowledgeEntries entries={filteredEntries as PartyKnowledgeEntry[]} />
                    : <DossierEntries entries={filteredEntries as PartyDossierEntry[]} />
                ) : entries.length ? (
                  <EmptySection copy="Try a broader search." title="No matching entries" />
                ) : (
                  <EmptySection copy={sectionEmptyCopy(section, selectedMember)} title={`No ${section} recorded`} />
                )}
              </>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
