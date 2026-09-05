"use client";

import { useEffect, useMemo, useState } from "react";

import type {
  PartyDossierEntry,
  PartyKnowledgeEntry,
  PartyMemberReadModel,
  PartySectionId,
} from "../data/hub-types";
import { markCharacterReady } from "../observability/performance.js";
import { CharacterOverview } from "./character/CharacterOverview";
import { CharacterSectionState } from "./character/CharacterSectionState";
import { CharacterSheet } from "./character/CharacterSheet";
import { CHARACTER_SECTIONS, CharacterShell } from "./character/CharacterShell";
import { InventoryTree } from "./character/InventoryTree";
import { WalletSummary } from "./character/WalletSummary";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";

function EmptySection({ title, copy }: { title: string; copy: string }) {
  return (
    <section className="character-empty-state">
      <span><Icon name="ScrollText" size={24} /></span>
      <div><strong>{title}</strong><p>{copy}</p></div>
    </section>
  );
}

function DossierEntries({ entries }: { entries: PartyDossierEntry[] }) {
  return (
    <div className="character-card-grid">
      {entries.map((entry) => (
        <article key={entry.id}>
          {entry.media ? <figure><MediaImage fallback={<Icon name="ScrollText" size={20} />} media={entry.media} /></figure> : null}
          <span>{entry.kind}</span><h3>{entry.title}</h3><p>{entry.detail}</p>
        </article>
      ))}
    </div>
  );
}

function KnowledgeEntries({ entries }: { entries: PartyKnowledgeEntry[] }) {
  return (
    <div className="character-card-grid">
      {entries.map((entry) => (
        <article key={entry.id}><span>{entry.kind} · {entry.stance}</span><p>{entry.text}</p></article>
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

function emptyCopy(section: PartySectionId, member: PartyMemberReadModel) {
  switch (section) {
    case "sheet": return "No canonical v2 character sheet is available for this character.";
    case "inventory": return member.inventoryStatus === "empty"
      ? "The canonical inventory is empty; no belongings are inferred from prose."
      : "No canonical inventory is available for this character.";
    case "knowledge": return member.isCurrent
      ? "No character knowledge is available from the authorized notebook."
      : "Character knowledge is private to an authorized player seat.";
    case "backstory": return "No biography or backstory has been recorded.";
    case "origin": return "No origin information has been recorded.";
    default: return "No information has been recorded.";
  }
}

function SectionHeader({ count, member, section }: {
  count: number;
  member: PartyMemberReadModel;
  section: PartySectionId;
}) {
  const state = section === "sheet" ? member.sheetState
    : section === "inventory" ? member.inventoryState : null;
  const unavailable = state?.status === "error" || state?.status === "forbidden" ||
    state?.status === "idle" || state?.status === "loading" && state.data === null;
  return (
    <header className="character-section-heading">
      <div><span className="eyebrow">{member.name}</span><h2>{CHARACTER_SECTIONS.find((candidate) => candidate.id === section)?.label}</h2></div>
      <p>{unavailable ? "Record count unavailable"
        : `${count} ${state?.status === "stale" ? "last confirmed" : "recorded"} ${count === 1 ? "entry" : "entries"}`}</p>
    </header>
  );
}

export function PartyView({
  loading = false,
  onRetry,
  party,
}: {
  loading?: boolean;
  onRetry?: () => void;
  party: PartyMemberReadModel[];
}) {
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
  useEffect(() => {
    if (!loading && selectedMember?.sheetState.status === "ready" && selectedMember.sheetState.source === "canonical") {
      markCharacterReady(selectedMember.id);
    }
  }, [loading, selectedMember]);

  const entries = selectedMember ? sectionEntries(selectedMember, section) : [];
  const state = selectedMember && (section === "sheet" || section === "inventory")
    ? (section === "sheet" ? selectedMember.sheetState : selectedMember.inventoryState)
    : null;
  const stateBlocksContent = state?.status === "empty" || state?.status === "error" ||
    state?.status === "forbidden" || state?.status === "idle" ||
    state?.status === "loading" && state.data === null;
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const filteredEntries = useMemo(() => entries.filter((entry) => {
    if (!normalizedQuery) return true;
    return ("text" in entry ? [entry.kind, entry.stance, entry.text] : [entry.kind, entry.title, entry.detail])
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  }), [entries, normalizedQuery]);

  if (!selectedMember) {
    return (
      <div className="character-page">
        <header className="character-page__heading"><div><span className="eyebrow">Campaign companions</span><h1 id="main-view-heading" tabIndex={-1}>The party</h1></div></header>
        <EmptySection copy="No party roster is available in this perspective." title="Party information unavailable" />
      </div>
    );
  }

  const selectSection = (next: PartySectionId) => { setSection(next); setQuery(""); };
  return (
    <CharacterShell
      onSelectMember={(id) => { setSelectedMemberId(id); selectSection("overview"); }}
      onSelectSection={selectSection}
      party={party}
      section={section}
      selectedMember={selectedMember}
    >
      {section === "overview" ? (
        <CharacterOverview member={selectedMember} onOpenSection={selectSection} />
      ) : (
        <>
          <SectionHeader count={entries.length} member={selectedMember} section={section} />
          {state ? <CharacterSectionState
            label={section === "sheet" ? "character sheet" : "inventory"}
            loading={loading}
            onRetry={onRetry}
            state={state}
          /> : null}
          {entries.length > 8 && section !== "sheet" && section !== "inventory" ? (
            <label className="character-search">
              <Icon name="Search" size={17} /><span className="sr-only">Search {section}</span>
              <input onChange={(event) => setQuery(event.target.value.slice(0, 80))} placeholder={`Search ${section}…`} type="search" value={query} />
            </label>
          ) : null}
          {stateBlocksContent ? null : section === "sheet" && selectedMember.characterSheet ? (
            <CharacterSheet sheet={selectedMember.characterSheet} />
          ) : stateBlocksContent ? null : section === "inventory" && selectedMember.characterSheet ? (
            <div className="character-inventory-layout">
              <InventoryTree
                definitions={selectedMember.characterSheet.dossier?.inventory.definitions ?? []}
                items={selectedMember.characterSheet.inventory.items}
              />
              <WalletSummary wallet={selectedMember.characterSheet.wallet} />
            </div>
          ) : filteredEntries.length ? (
            section === "knowledge"
              ? <KnowledgeEntries entries={filteredEntries as PartyKnowledgeEntry[]} />
              : <DossierEntries entries={filteredEntries as PartyDossierEntry[]} />
          ) : entries.length ? (
            <EmptySection copy="Try a broader search." title="No matching entries" />
          ) : (
            <EmptySection copy={emptyCopy(section, selectedMember)} title={`No ${section} recorded`} />
          )}
        </>
      )}
    </CharacterShell>
  );
}
