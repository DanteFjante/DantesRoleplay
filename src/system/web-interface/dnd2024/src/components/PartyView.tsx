"use client";

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { objectConsumers } from "../data/scoped-change-stream";
import type { InventoryReturnContext } from "../data/item-view-route";

import type {
  InventoryContainerResult,
  InventoryContainerPageResult,
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
    case "registry": return [];
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
    case "registry": return "The Party registry is unavailable.";
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
        : section === "inventory" ? `${count} loaded top-level ${count === 1 ? "item" : "items"}`
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
  loadInventoryContainer,
  navigationCharacterId,
  navigationSection,
  onNavigationChange,
  inventoryReturn,
  onOpenItem,
  summaryOnly = false,
  confirmedOwner = false,
}: {
  loading?: boolean;
  onRetry?: () => void;
  party: PartyMemberReadModel[];
  loadCharacterSheet?: (id: string, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  loadCharacterDetails?: (id: string, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  loadCharacterInventory?: (id: string, signal: AbortSignal) => Promise<InventoryContainerResult>;
  loadInventoryContainer?: (actorId: string, containerId: string, signal: AbortSignal) => Promise<InventoryContainerPageResult>;
  navigationCharacterId?: string;
  navigationSection?: PartySectionId;
  onNavigationChange?: (id: string, section: PartySectionId, replace?: boolean) => void;
  inventoryReturn?: InventoryReturnContext | null;
  onOpenItem?: (characterId: string, itemId: string, context: InventoryReturnContext) => void;
  summaryOnly?: boolean;
  /** Standalone fixture compatibility only; the connected hub passes true and owns values in Redux. */
  confirmedOwner?: boolean;
}) {
  const requestedMemberId = navigationCharacterId && party.some((member) => member.id === navigationCharacterId)
    ? navigationCharacterId
    : party[0]?.id ?? "";
  const [localSelectedMemberId, setLocalSelectedMemberId] = useState(requestedMemberId);
  const [localSection, setLocalSection] = useState<PartySectionId>(navigationSection ?? "overview");
  const selectedMemberId = onNavigationChange ? requestedMemberId : localSelectedMemberId;
  const selectedActorPresent = party.some((member) => member.id === selectedMemberId);
  const section = summaryOnly ? "overview" : onNavigationChange ? navigationSection ?? "overview" : localSection;
  const [expandedIds, setExpandedIds] = useState<string[]>(inventoryReturn?.expandedIds ?? []);
  const [query, setQuery] = useState(inventoryReturn?.query ?? "");
  const [detailKind, setDetailKind] = useState<"sheet" | "details" | null>(null);
  const [fallbackDetail, setFallbackDetail] = useState<PartyMemberReadModel | null>(null);
  const [detailBusy, setDetailBusy] = useState(false);
  const [detailErrorKind, setDetailErrorKind] = useState<"sheet" | "details" | null>(null);
  const [inventoryRequested, setInventoryRequested] = useState(false);
  // A failed read is an attempt for this particular owner, but not a confirmed
  // inventory. Keep that distinction so it neither loops nor blocks a new
  // scope/generation from trying again.
  const [inventoryAttempted, setInventoryAttempted] = useState(false);
  const [fallbackInventory, setFallbackInventory] = useState<InventoryContainerResult | null>(null);
  const [inventoryBusy, setInventoryBusy] = useState(false);
  const [inventoryError, setInventoryError] = useState(false);
  const [retry, setRetry] = useState(0);
  const previousDetailLoaders = useRef({ sheet: loadCharacterSheet, details: loadCharacterDetails });
  const previousInventoryLoader = useRef(loadCharacterInventory);
  const detailRequest = useRef(0);
  const inventoryRequest = useRef(0);
  const previousRoute = useRef(`${selectedMemberId}:${section}`);
  const requiredDetailKind: "sheet" | "details" | null = !summaryOnly &&
    (section === "overview" || section === "sheet") ? "sheet"
    : !summaryOnly && section !== "inventory" ? "details" : null;
  useEffect(() => {
    setDetailKind(null);
    setFallbackDetail(null);
    setDetailErrorKind(null);
  }, [selectedMemberId]);
  useEffect(() => {
    const previous = previousDetailLoaders.current;
    previousDetailLoaders.current = { sheet: loadCharacterSheet, details: loadCharacterDetails };
    if (previous.sheet === loadCharacterSheet && previous.details === loadCharacterDetails) return;
    // A changed callback is an owner generation/scope boundary. Do not let an
    // earlier success or failure suppress the active character's new owner.
    setDetailKind(null);
    setFallbackDetail(null);
    setDetailErrorKind(null);
  }, [loadCharacterDetails, loadCharacterSheet]);
  useEffect(() => {
    if (previousInventoryLoader.current === loadCharacterInventory) return;
    previousInventoryLoader.current = loadCharacterInventory;
    setInventoryRequested(false);
    setInventoryAttempted(false);
    setFallbackInventory(null);
    setInventoryError(false);
  }, [loadCharacterInventory]);
  useEffect(() => {
    const invalidate = () => {
      setDetailKind(null);
      setFallbackDetail(null);
      setInventoryRequested(false);
      setInventoryAttempted(false);
      setFallbackInventory(null);
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
    const loader = requiredDetailKind === "sheet" ? loadCharacterSheet
      : requiredDetailKind === "details" ? loadCharacterDetails : null;
    if (!requiredDetailKind || !loader || !selectedMemberId ||
        !selectedActorPresent ||
        detailKind === "details" || detailKind === requiredDetailKind) return;
    const controller = new AbortController();
    const request = ++detailRequest.current;
    setDetailKind(null);
    setDetailErrorKind(null);
    setDetailBusy(true);
    void loader(selectedMemberId, controller.signal).then((value) => {
      if (controller.signal.aborted) return;
      if (value.id !== selectedMemberId) {
        setDetailErrorKind(requiredDetailKind);
        return;
      }
      // Standalone fixtures have no Redux facet. Retain the returned result,
      // including a denial, so its precise authority and media outcome remain
      // visible instead of falling back to an older roster projection.
      if (!confirmedOwner) setFallbackDetail(value);
      if (value.sheetState.status === "ready" || value.sheetState.status === "empty") {
        setDetailKind(requiredDetailKind);
      } else setDetailErrorKind(requiredDetailKind);
    }).catch(() => {
      if (!controller.signal.aborted) setDetailErrorKind(requiredDetailKind);
    }).finally(() => {
      if (detailRequest.current === request) setDetailBusy(false);
    });
    return () => {
      controller.abort();
      if (detailRequest.current === request) setDetailBusy(false);
    };
  }, [confirmedOwner, loadCharacterDetails, loadCharacterSheet, selectedMemberId, selectedActorPresent, requiredDetailKind, retry, detailKind]);

  useEffect(() => {
    if (section !== "inventory" || !loadCharacterInventory || !selectedMemberId ||
        !selectedActorPresent ||
        inventoryRequested || inventoryAttempted) return;
    const controller = new AbortController();
    const request = ++inventoryRequest.current;
    setInventoryError(false);
    setInventoryBusy(true);
    void loadCharacterInventory(selectedMemberId, controller.signal).then((value) => {
      if (controller.signal.aborted) return;
      if (value.status === "ready" && value.data.container.id !== selectedMemberId) {
        setInventoryError(true);
        setInventoryAttempted(true);
        return;
      }
      // A returned error/forbidden response carries useful, precise wallet and
      // authorization evidence. It is not a confirmed completion, but it is an
      // attempt for this loader until an invalidation or new owner arrives.
      if (!confirmedOwner) setFallbackInventory(value);
      setInventoryAttempted(true);
      if (value.status === "ready") setInventoryRequested(true);
    }).catch(() => {
      if (!controller.signal.aborted) {
        setInventoryError(true);
        setInventoryAttempted(true);
      }
    }).finally(() => {
      if (inventoryRequest.current === request) setInventoryBusy(false);
    });
    return () => {
      controller.abort();
      if (inventoryRequest.current === request) setInventoryBusy(false);
    };
  }, [confirmedOwner, inventoryAttempted, inventoryRequested, loadCharacterInventory, selectedActorPresent, retry, section, selectedMemberId]);

  useEffect(() => {
    if (!onNavigationChange && !party.some((member) => member.id === selectedMemberId)) {
      setLocalSelectedMemberId(party[0]?.id ?? "");
      setInventoryRequested(false);
      setInventoryAttempted(false);
      setFallbackInventory(null);
      setInventoryError(false);
      setLocalSection("overview");
      setQuery("");
    }
  }, [party, selectedMemberId, onNavigationChange]);

  useEffect(() => {
    if (onNavigationChange && selectedMemberId && navigationCharacterId !== selectedMemberId) {
      onNavigationChange(selectedMemberId, "overview", true);
    }
  }, [navigationCharacterId, onNavigationChange, selectedMemberId]);

  useEffect(() => {
    setExpandedIds(inventoryReturn?.characterId === selectedMemberId ? inventoryReturn.expandedIds : []);
    setInventoryRequested(false);
    setInventoryAttempted(false);
    setFallbackInventory(null);
    setInventoryError(false);
    setQuery(inventoryReturn?.characterId === selectedMemberId ? inventoryReturn.query : "");
  }, [inventoryReturn, selectedMemberId]);

  useLayoutEffect(() => {
    if (!onNavigationChange) return;
    const route = `${selectedMemberId}:${section}`;
    if (route === previousRoute.current) return;
    previousRoute.current = route;
    document.querySelector<HTMLElement>(`.character-tabs [data-character-section="${section}"]`)
      ?.focus({ preventScroll: true });
  }, [onNavigationChange, section, selectedMemberId]);

  const selectedMember = !confirmedOwner && fallbackDetail?.id === selectedMemberId
    ? fallbackDetail : party.find((member) => member.id === selectedMemberId);
  const displayedParty = useMemo(() => party.map((member) => member.id === selectedMemberId && selectedMember
    ? selectedMember
    : member), [party, selectedMember, selectedMemberId]);
  const retryCharacter = () => {
    setDetailKind(null);
    setFallbackDetail(null);
    setDetailErrorKind(null);
    setRetry((value) => value + 1);
  };
  const retryInventory = () => {
    setInventoryRequested(false);
    setInventoryAttempted(false);
    setFallbackInventory(null);
    setInventoryError(false);
    setRetry((value) => value + 1);
  };
  const inventoryResult = confirmedOwner ? selectedMember?.inventoryResource ?? null : fallbackInventory;
  const inventoryData = inventoryResult?.status === "ready" ? inventoryResult.data : null;
  const failedInventoryWallet = inventoryResult && inventoryResult.status !== "ready"
    ? { wallet: inventoryResult.wallet ?? null, state: inventoryResult.walletState } : null;
  const walletOnly = Boolean(failedInventoryWallet?.state);
  useEffect(() => {
    if (!loading && selectedMember?.sheetState.status === "ready" && selectedMember.sheetState.source === "canonical") {
      markCharacterReady(selectedMember.id);
    }
  }, [loading, selectedMember]);

  const entries = selectedMember ? sectionEntries(selectedMember, section) : [];
  const sectionCount = section === "inventory"
    ? inventoryData?.items.length ?? selectedMember?.characterSheet?.inventory?.items.length ?? 0
    : entries.length;
  const inventoryState = loadCharacterInventory
    ? inventoryBusy ? { status: "loading" as const, data: null }
      : inventoryError ? { status: "error" as const, data: null, failureCategory: "transport" as const,
        diagnosticId: `inventory-${selectedMemberId}-transport` }
      : inventoryResult?.status === "ready"
        // The inventory tree owns its empty/partial message; the independently read wallet
        // must remain visible even when the item collection is confirmed empty.
        ? { status: "ready" as const, data: [], source: "canonical" as const }
        : inventoryResult ?? { status: "idle" as const, data: null }
    : selectedMember?.inventoryState ?? null;
  const state = selectedMember && (section === "sheet" || section === "inventory")
    ? (section === "sheet" ? selectedMember.sheetState : inventoryState)
    : null;
  const stateBlocksContent = !walletOnly && (state?.status === "empty" || state?.status === "error" ||
    state?.status === "forbidden" || state?.status === "idle" ||
    state?.status === "loading" && state.data === null);
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const filteredEntries = useMemo(() => entries.filter((entry) => {
    if (!normalizedQuery) return true;
    return ("text" in entry ? [entry.kind, entry.stance, entry.text] : [entry.kind, entry.title, entry.detail])
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  }), [entries, normalizedQuery]);
  const detailFailed = requiredDetailKind !== null && detailErrorKind === requiredDetailKind;
  const overviewPending = section === "overview" && !summaryOnly && Boolean(loadCharacterSheet) && !detailFailed &&
    detailKind !== "sheet" && detailKind !== "details";

  if (!selectedMember) {
    return (
      <div className="character-page">
        <header className="character-page__heading"><div><span className="eyebrow">Campaign companions</span><h1 id="main-view-heading" tabIndex={-1}>The party</h1></div></header>
        <EmptySection copy="No party roster is available in this perspective." title="Party information unavailable" />
      </div>
    );
  }

  const selectSection = (next: PartySectionId) => {
    const normalized = summaryOnly && next !== "registry" ? "overview" : next;
    if (onNavigationChange) onNavigationChange(selectedMemberId, normalized);
    else setLocalSection(normalized);
    setQuery("");
  };
  return (
    <CharacterShell
      onSelectMember={(id) => {
        if (onNavigationChange) onNavigationChange(id, "overview");
        else {
          setLocalSelectedMemberId(id);
          setLocalSection("overview");
        }
        setExpandedIds([]);
        setInventoryRequested(false);
        setInventoryAttempted(false);
        setFallbackInventory(null);
        setInventoryError(false);
        setQuery("");
      }}
      onSelectSection={selectSection}
      party={displayedParty}
      sections={summaryOnly ? CHARACTER_SECTIONS.filter((candidate) => candidate.id === "overview" || candidate.id === "registry") : undefined}
      section={section}
      selectedMember={selectedMember}
    >
      {section === "overview" ? (
        overviewPending ? <CharacterOverviewSkeleton name={selectedMember.name} />
          : detailFailed ? <div className="character-state" role="alert">
            <div><strong>Character overview unavailable</strong>
              <p>This character could not be loaded. No missing details are inferred.</p></div>
            <button type="button" onClick={retryCharacter}>Retry character</button>
          </div>
            : <CharacterOverview member={selectedMember} onOpenSection={selectSection} summaryOnly={summaryOnly} />
      ) : (
        <>
          {detailBusy ? <p role="status">Loading this character’s dossier…</p> : null}
          {detailFailed ? <div className="character-state" role="alert">
            <div><strong>Character section unavailable</strong>
              <p>This character could not be loaded. No missing details are inferred.</p></div>
            <button type="button" onClick={retryCharacter}>Retry character</button>
          </div> : null}
          <SectionHeader count={sectionCount} member={selectedMember} section={section} sectionState={state} />
          {state ? <CharacterSectionState
            label={section === "sheet" ? "character sheet" : "inventory"}
            // Inventory is independently confirmed. A slow header/sheet read
            // must not replace its already-authorized rows or wallet with a
            // generic character loading skeleton.
            loading={section === "inventory" ? inventoryBusy : loading || detailBusy}
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
          ) : stateBlocksContent ? null : section === "inventory" && (inventoryData || selectedMember.characterSheet || walletOnly) ? (
            <div className="character-inventory-layout">
              {inventoryData || selectedMember.characterSheet ? <InventoryTree
                key={selectedMember.id}
                subjectId={selectedMember.id}
                sourceRevision={inventoryData?.projection?.sourceRevisionFingerprint ?? selectedMember.characterSheet?.projection?.sourceRevisionFingerprint}
                expandedIds={expandedIds}
                onExpandedChange={(id, expanded) => setExpandedIds((previous) => expanded
                  ? previous.includes(id) ? previous : [...previous, id] : previous.filter((value) => value !== id))}
                onOpenItem={onOpenItem ? (itemId) => onOpenItem(selectedMember.id, itemId, {
                  kind: "inventory", characterId: selectedMember.id, expandedIds, query,
                  focusItemId: itemId, scrollY: window.scrollY,
                }) : undefined}
                items={inventoryData?.items ?? selectedMember.characterSheet?.inventory?.items ?? []}
                reasons={inventoryData?.reasons ?? []}
                notices={inventoryData?.notices ?? []}
                query={query}
                onQueryChange={setQuery}
                restore={inventoryReturn?.characterId === selectedMember.id ? inventoryReturn : null}
                loadContainer={loadInventoryContainer ? (containerId, signal) =>
                  loadInventoryContainer(selectedMember.id, containerId, signal) : undefined}
              /> : null}
              <WalletSummary status={inventoryData?.walletState.status ?? failedInventoryWallet?.state?.status ?? "complete"}
                reason={inventoryData?.walletState.reason ?? failedInventoryWallet?.state?.reason}
                wallet={inventoryData ? inventoryData.wallet : failedInventoryWallet
                  ? failedInventoryWallet.wallet : selectedMember.characterSheet?.wallet ?? null} />
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

function CharacterOverviewSkeleton({ name }: { name: string }) {
  return (
    <section aria-busy="true" className="character-overview-skeleton" role="status">
      <span className="sr-only">Loading {name}&apos;s character overview</span>
      <div className="character-overview-skeleton__lead">
        <span />
        <strong />
        <p />
        <p />
      </div>
      <div className="character-overview-skeleton__facts">
        {[0, 1, 2, 3].map((value) => <span key={value} />)}
      </div>
    </section>
  );
}

/** Compatibility name for fixtures while production imports the Character feature directly. */
export const PartyView = CharacterWorkspace;
