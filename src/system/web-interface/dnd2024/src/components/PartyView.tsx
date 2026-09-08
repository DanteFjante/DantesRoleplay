"use client";

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { objectConsumers } from "../data/scoped-change-stream";
import type { InventoryReturnContext } from "../data/item-view-route";

import type {
  InventoryContainerResult,
  PartyDossierEntry,
  PartyKnowledgeEntry,
  PartyMemberReadModel,
  PartySectionId,
  SectionState,
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

function SectionHeader({ count, member, section, sectionState }: {
  count: number;
  member: PartyMemberReadModel;
  section: PartySectionId;
  sectionState?: SectionState<unknown> | null;
}) {
  const state = sectionState ?? (section === "sheet" ? member.sheetState
    : section === "inventory" ? member.inventoryState : null);
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

export function CharacterWorkspace({
  loading = false,
  onRetry,
  party,
  loadCharacterSheet,
  loadCharacterDetails,
  loadCharacterInventory,
  navigationCharacterId,
  inventoryReturn,
  onOpenItem,
}: {
  loading?: boolean;
  onRetry?: () => void;
  party: PartyMemberReadModel[];
  loadCharacterSheet?: (id: string, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  loadCharacterDetails?: (id: string, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  loadCharacterInventory?: (id: string, signal: AbortSignal) => Promise<InventoryContainerResult>;
  navigationCharacterId?: string;
  inventoryReturn?: InventoryReturnContext | null;
  onOpenItem?: (characterId: string, itemId: string, context: InventoryReturnContext) => void;
}) {
  const [selectedMemberId, setSelectedMemberId] = useState(navigationCharacterId ?? party[0]?.id ?? "");
  const [section, setSection] = useState<PartySectionId>(navigationCharacterId ? "inventory" : "overview");
  const [expandedIds, setExpandedIds] = useState<string[]>(inventoryReturn?.expandedIds ?? []);
  const restored = useRef(false);
  const [query, setQuery] = useState("");
  const [detail, setDetail] = useState<PartyMemberReadModel | null>(null);
  const [detailKind, setDetailKind] = useState<"sheet" | "details" | null>(null);
  const [detailBusy, setDetailBusy] = useState(false);
  const [detailError, setDetailError] = useState(false);
  const [inventoryResult, setInventoryResult] = useState<InventoryContainerResult | null>(null);
  const [inventoryBusy, setInventoryBusy] = useState(false);
  const [inventoryError, setInventoryError] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const invalidate = () => {
      setDetail(null);
      setDetailKind(null);
      setInventoryResult(null);
      setRetry((value) => value + 1);
    };
    const changed = (event: Event) => {
      if (objectConsumers((event as CustomEvent).detail?.object?.qualifiedId).character) invalidate();
    };
    window.addEventListener("dnd2024-view-invalidated", invalidate);
    window.addEventListener("dnd2024-object-changed", changed);
    return () => {
      window.removeEventListener("dnd2024-view-invalidated", invalidate);
      window.removeEventListener("dnd2024-object-changed", changed);
    };
  }, []);
  useEffect(() => {
    const requiredKind = section === "sheet" ? "sheet"
      : section === "overview" || section === "inventory" ? null : "details";
    const loader = requiredKind === "sheet" ? loadCharacterSheet
      : requiredKind === "details" ? loadCharacterDetails : null;
    if (!requiredKind || !loader || !selectedMemberId ||
        !party.some((member) => member.id === selectedMemberId) ||
        detail?.id === selectedMemberId && (detailKind === "details" || detailKind === requiredKind)) return;
    const controller = new AbortController();
    setDetail(null);
    setDetailKind(null);
    setDetailError(false);
    setDetailBusy(true);
    void loader(selectedMemberId, controller.signal).then((value) => {
      if (!controller.signal.aborted && value.id === selectedMemberId) {
        setDetail(value);
        setDetailKind(requiredKind);
      }
    }).catch(() => {
      if (!controller.signal.aborted) setDetailError(true);
    }).finally(() => {
      if (!controller.signal.aborted) setDetailBusy(false);
    });
    return () => controller.abort();
  }, [loadCharacterDetails, loadCharacterSheet, selectedMemberId, section, retry, detail, detailKind, party]);

  useEffect(() => {
    if (section !== "inventory" || !loadCharacterInventory || !selectedMemberId ||
        !party.some((member) => member.id === selectedMemberId) ||
        inventoryResult) return;
    const controller = new AbortController();
    setInventoryResult(null);
    setInventoryError(false);
    setInventoryBusy(true);
    void loadCharacterInventory(selectedMemberId, controller.signal).then((value) => {
      if (!controller.signal.aborted && (value.status !== "ready" || value.data.owner.id === selectedMemberId)) {
        setInventoryResult(value);
      }
    }).catch(() => {
      if (!controller.signal.aborted) setInventoryError(true);
    }).finally(() => {
      if (!controller.signal.aborted) setInventoryBusy(false);
    });
    return () => controller.abort();
  }, [inventoryResult, loadCharacterInventory, party, retry, section, selectedMemberId]);

  useEffect(() => {
    if (!navigationCharacterId && !party.some((member) => member.id === selectedMemberId)) {
      setSelectedMemberId(party[0]?.id ?? "");
      setInventoryResult(null);
      setSection("overview");
      setQuery("");
    }
  }, [party, selectedMemberId, navigationCharacterId]);

  const selectedMember = detail?.id === selectedMemberId ? detail
    : party.find((member) => member.id === selectedMemberId);
  const retryCharacter = () => {
    setDetail(null);
    setDetailKind(null);
    setRetry((value) => value + 1);
  };
  const retryInventory = () => {
    setInventoryResult(null);
    setInventoryError(false);
    setRetry((value) => value + 1);
  };
  const inventoryData = inventoryResult?.status === "ready" ? inventoryResult.data : null;
  useLayoutEffect(() => {
    if (restored.current || !inventoryReturn || inventoryBusy || section !== "inventory" ||
        !(inventoryData || !loadCharacterInventory && selectedMember?.characterSheet)) return;
    const target = [...document.querySelectorAll<HTMLElement>("[data-item-open]")]
      .find((element) => element.dataset.itemOpen === inventoryReturn.focusItemId);
    if (!target) return;
    restored.current = true;
    target.focus({ preventScroll: true });
    window.scrollTo(0, inventoryReturn.scrollY);
  }, [inventoryReturn, inventoryBusy, section, selectedMember, inventoryData, loadCharacterInventory]);
  useEffect(() => {
    if (!loading && selectedMember?.sheetState.status === "ready" && selectedMember.sheetState.source === "canonical") {
      markCharacterReady(selectedMember.id);
    }
  }, [loading, selectedMember]);

  const entries = selectedMember ? sectionEntries(selectedMember, section) : [];
  const sectionCount = section === "inventory"
    ? inventoryData?.items.length ?? selectedMember?.characterSheet?.inventory.items.length ?? 0
    : entries.length;
  const inventoryState = loadCharacterInventory
    ? inventoryBusy ? { status: "loading" as const, data: null }
      : inventoryError ? { status: "error" as const, data: null, failureCategory: "transport" as const,
        diagnosticId: `inventory-${selectedMemberId}-transport` }
      : inventoryResult?.status === "ready"
        ? { status: inventoryResult.data.items.length || inventoryResult.data.wallet.coinCount ? "ready" as const : "empty" as const,
          data: [], source: "canonical" as const }
        : inventoryResult ?? { status: "idle" as const, data: null }
    : selectedMember?.inventoryState ?? null;
  const state = selectedMember && (section === "sheet" || section === "inventory")
    ? (section === "sheet" ? selectedMember.sheetState : inventoryState)
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
      onSelectMember={(id) => { setSelectedMemberId(id); setExpandedIds([]); setInventoryResult(null); selectSection("overview"); }}
      onSelectSection={selectSection}
      party={party}
      section={section}
      selectedMember={selectedMember}
    >
      {detailBusy ? <p role="status">Loading this character’s authorized dossier…</p> : null}
      {detailError ? <div role="alert"><p>This character could not be loaded. No empty inventory or wallet is inferred.</p>
        <button type="button" onClick={retryCharacter}>Retry character</button></div> : null}
      {section === "overview" ? (
        <CharacterOverview member={selectedMember} onOpenSection={selectSection} />
      ) : (
        <>
          <SectionHeader count={sectionCount} member={selectedMember} section={section} sectionState={state} />
          {state ? <CharacterSectionState
            label={section === "sheet" ? "character sheet" : "inventory"}
            loading={loading || detailBusy}
            onRetry={section === "inventory" && loadCharacterInventory ? retryInventory
              : loadCharacterSheet || loadCharacterDetails ? retryCharacter : onRetry}
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
          ) : stateBlocksContent ? null : section === "inventory" && (inventoryData || selectedMember.characterSheet) ? (
            <div className="character-inventory-layout">
              <InventoryTree
                key={selectedMember.id}
                expandedIds={expandedIds}
                onExpandedChange={(id, expanded) => setExpandedIds((previous) => expanded
                  ? previous.includes(id) ? previous : [...previous, id] : previous.filter((value) => value !== id))}
                onOpenItem={onOpenItem ? (itemId) => onOpenItem(selectedMember.id, itemId, {
                  characterId: selectedMember.id, expandedIds, focusItemId: itemId, scrollY: window.scrollY,
                }) : undefined}
                complete={inventoryData?.limits.complete ?? true}
                definitions={[]}
                items={inventoryData?.items ?? selectedMember.characterSheet?.inventory.items ?? []}
                reasons={inventoryData?.reasons ?? []}
              />
              <WalletSummary complete={inventoryData?.limits.complete ?? true}
                wallet={inventoryData?.wallet ?? selectedMember.characterSheet!.wallet} />
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

/** Compatibility name for fixtures while production imports the Character feature directly. */
export const PartyView = CharacterWorkspace;
