"use client";

import { lazy, Suspense, useCallback, useEffect, useReducer, useRef, useState } from "react";

import {
  CAMPAIGN_SUMMARY_OBJECT_ID,
  CAMPAIGN_LOCATION_VISITS_OBJECT_ID,
  FACTION_DIRECTORY_OBJECT_ID,
  WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID,
  type FactionDirectoryPage,
} from "../data/object-resources";
import { createHubObjectUiState, hubObjectUiReducer } from "../data/hub-object-ui";
import { resolveCampaignWorldTarget } from "../data/campaign-navigation";
import { HUB_ROUTE_EVENT, navigateHubRoute, parseHubRoute } from "../data/hub-route";
import { ITEM_ROUTE_EVENT, navigateItemRoute, parseItemRoute, readInventoryReturn } from "../data/item-view-route";
import { applyDeferredHubUpdate, preserveLastGoodPartyData } from "../data/section-state";
import { ViewReadError } from "../data/view-read-client";
import type {
  CampaignSectionId,
  CampaignReadModel,
  DeferredHubSection,
  DeferredHubUpdate,
  DeferredViewState,
  HubContextSelection,
  LocationSectionId,
  MainTabId,
  Perspective,
  ReadyHubEnvelope,
  RuleReadModel,
  WorldLocation,
  WorldSectionId,
} from "../data/hub-types";
import {
  filterLocations,
  isReadyHubEnvelope,
  normalizeCampaignSection,
  normalizeMainTab,
  normalizeLocationSection,
  normalizePerspective,
  normalizeWorldSection,
  normalizeMapId,
  resolveCurrentSceneLocation,
  resolveMapDocument,
  resolveSelectedLocation,
  resolveSelectedMapFeature,
} from "../state.js";
import { MainNavigation } from "./MainNavigation";
import type { InstalledContentModel } from "../server/effective-content";
import type { ItemViewClient } from "../server/item-view-client";
import type { CampaignPremiseWriter } from "../server/campaign-premise-write";
import { TopBar } from "./TopBar";
import { WorldView } from "./WorldView";
import { markActiveViewReady } from "../observability/performance.js";
import { ViewErrorBoundary } from "./ViewErrorBoundary";

const PERSPECTIVE_KEY = "dnd2024-table-mode";
const CAMPAIGN_KEY = "dnd2024-table-campaign";
const CampaignView = lazy(() => import("./CampaignView")
  .then((module) => ({ default: module.CampaignView })));
const InstalledContentView = lazy(() => import("./InstalledContentView")
  .then((module) => ({ default: module.InstalledContentView })));
const ItemWorkspace = lazy(() => import("./items/ItemWorkspaceFeature")
  .then((module) => ({ default: module.ItemWorkspace })));
const CharacterWorkspace = lazy(() => import("./character/CharacterWorkspaceFeature")
  .then((module) => ({ default: module.CharacterWorkspace })));
const PlayConversationPanel = lazy(() => import("./PlayConversationPanel")
  .then((module) => ({ default: module.PlayConversationPanel })));
const CurrentViewPreview = lazy(() => import("./PreviewViewsFeature")
  .then((module) => ({ default: module.CurrentViewPreview })));
const RulesView = lazy(() => import("./RulesView")
  .then((module) => ({ default: module.RulesView })));

function ViewLoading({ label }: { label: string }) {
  return (
    <section aria-busy="true" className="view-loading" role="status">
      <span className="eyebrow">{label}</span>
      <h1 id="main-view-heading" tabIndex={-1}>Opening {label.toLocaleLowerCase()}</h1>
      <p>The current authorized view is loading.</p>
    </section>
  );
}

function ActorBindingRequired() {
  return <section className="view-unavailable" role="status"
    data-view-status="unavailable" data-reason-code="audience-restricted">
    <h1 id="main-view-heading" tabIndex={-1}>Actor binding required</h1>
    <p>This view is unavailable in Player preview. Open it from an authorized Actor seat.</p>
  </section>;
}

function loadRequestedPerspective(): Perspective | null {
  try {
    const stored = window.localStorage.getItem(PERSPECTIVE_KEY);
    return stored === null ? null : (normalizePerspective(stored) as Perspective);
  } catch {
    return null;
  }
}

function saveEffectivePerspective(perspective: Perspective) {
  try {
    window.localStorage.setItem(PERSPECTIVE_KEY, perspective);
  } catch {
    // A blocked preference store never blocks the information hub.
  }
}

function loadRequestedCampaignId(): string | null {
  try {
    const stored = window.localStorage.getItem(CAMPAIGN_KEY);
    return stored && stored.length <= 200 && stored === stored.trim() && !/\s/u.test(stored)
      ? stored
      : null;
  } catch {
    return null;
  }
}

function saveSelectedCampaign(campaignId: string) {
  try {
    window.localStorage.setItem(CAMPAIGN_KEY, campaignId);
  } catch {
    // A blocked preference store never blocks context switching.
  }
}

type HubEnvelopeLoader = (
  perspective: Perspective,
  campaignId: string,
  preferCached: boolean,
) => Promise<ReadyHubEnvelope>;

type RulesLoader = () => Promise<RuleReadModel[]>;
type ContentLoader = () => Promise<InstalledContentModel>;
type FactionPageLoader = (
  envelope: ReadyHubEnvelope,
  cursor: string | null,
  signal: AbortSignal,
) => Promise<FactionDirectoryPage>;
type CampaignDetailsLoader = (envelope: ReadyHubEnvelope, signal: AbortSignal) => Promise<CampaignReadModel>;

export function DndInformationHub({
  initialEnvelope,
  loadEnvelope,
  loadRules,
  loadContent,
  loadCharacterSheet,
  loadCharacterDetails,
  loadCharacterInventory,
  loadFactionPage,
  loadCampaignDetails,
  loadDeferredSection,
  loadWorldScope,
  writeCampaignPremise,
  subscribeChanges,
}: {
  initialEnvelope: ReadyHubEnvelope;
  loadEnvelope?: HubEnvelopeLoader;
  loadRules?: RulesLoader;
  loadContent: ContentLoader;
  loadCharacterSheet?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").PartyMemberReadModel>;
  loadCharacterDetails?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").PartyMemberReadModel>;
  loadCharacterInventory?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").InventoryContainerResult>;
  loadFactionPage?: FactionPageLoader;
  loadCampaignDetails?: CampaignDetailsLoader;
  loadDeferredSection?: (envelope: ReadyHubEnvelope, section: DeferredHubSection, signal: AbortSignal) => Promise<DeferredHubUpdate>;
  loadWorldScope?: (envelope: ReadyHubEnvelope, scopeId: string, signal: AbortSignal) => Promise<Extract<DeferredHubUpdate, { section: "locations" }>>;
  writeCampaignPremise?: CampaignPremiseWriter;
  subscribeChanges?: (envelope: ReadyHubEnvelope) => () => void;
}) {
  const [envelope, setEnvelope] = useState(initialEnvelope);
  const itemClientScope = `${envelope.applicationId}:${envelope.stateSpaceId}:${envelope.revision}:${envelope.audience.seat}:${envelope.audience.perspective}`;
  const itemClientScopeRef = useRef(itemClientScope);
  itemClientScopeRef.current = itemClientScope;
  const itemClientCache = useRef<{ scope: string; client: ItemViewClient } | null>(null);
  const retainItemClient = useCallback((client: ItemViewClient) => {
    const previous = itemClientCache.current;
    if (previous && previous.client !== client) previous.client.invalidate();
    itemClientCache.current = { scope: itemClientScopeRef.current, client };
  }, []);
  useEffect(() => () => itemClientCache.current?.client.invalidate(), []);
  const readCharacterSheet = useCallback((id: string, signal: AbortSignal) => {
    if (!loadCharacterSheet) throw new Error("Character sheet loading is unavailable.");
    return loadCharacterSheet(envelope, id, signal);
  }, [envelope, loadCharacterSheet]);
  const readCharacterDetails = useCallback((id: string, signal: AbortSignal) => {
    if (!loadCharacterDetails) throw new Error("Character detail loading is unavailable.");
    return loadCharacterDetails(envelope, id, signal);
  }, [envelope, loadCharacterDetails]);
  const readCharacterInventory = useCallback((id: string, signal: AbortSignal) => {
    if (!loadCharacterInventory) throw new Error("Character inventory loading is unavailable.");
    return loadCharacterInventory(envelope, id, signal);
  }, [envelope, loadCharacterInventory]);
  const [itemRoute, setItemRoute] = useState(() => parseItemRoute(window.location.hash));
  const [activeTab, setActiveTab] = useState<MainTabId>(() => {
    const item = parseItemRoute(window.location.hash);
    if (item.kind !== "none") return "party";
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.tab : "world";
  });
  const [campaignSection, setCampaignSection] = useState<CampaignSectionId>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.campaignSection : "overview";
  });
  useEffect(() => {
    const changed = () => {
      const next = parseItemRoute(window.location.hash);
      setItemRoute(next);
      if (next.kind !== "none") setActiveTab("party");
      else {
        const hub = parseHubRoute(window.location.hash);
        if (hub.kind === "hub") {
          setActiveTab(hub.tab);
          setCampaignSection(hub.campaignSection);
        } else if (window.history.state?.itemMainTab) {
          setActiveTab(normalizeMainTab(window.history.state.itemMainTab) as MainTabId);
        }
      }
    };
    for (const event of ["popstate", "hashchange", ITEM_ROUTE_EVENT, HUB_ROUTE_EVENT])
      window.addEventListener(event, changed);
    return () => {
      for (const event of ["popstate", "hashchange", ITEM_ROUTE_EVENT, HUB_ROUTE_EVENT])
        window.removeEventListener(event, changed);
    };
  }, []);
  const [worldSection, setWorldSection] = useState<WorldSectionId>("overview");
  const [locationSection, setLocationSection] = useState<LocationSectionId>("details");
  const [selectedLocationId, setSelectedLocationId] = useState(initialEnvelope.world.currentLocationId);
  const [objectUi, dispatchObjectUi] = useReducer(
    hubObjectUiReducer,
    initialEnvelope.world.factions[0]?.id ?? "",
    createHubObjectUiState,
  );
  const { selectedFactionId, campaignDetailsLoaded } = objectUi;
  const [selectedPersonId, setSelectedPersonId] = useState(initialEnvelope.world.people[0]?.id ?? "");
  const [activeMapId, setActiveMapId] = useState(
    normalizeMapId(initialEnvelope.world.maps, initialEnvelope.world.rootMapId, initialEnvelope.world.rootMapId) as string,
  );
  const [selectedMapFeatureId, setSelectedMapFeatureId] = useState("");
  const [locationQuery, setLocationQuery] = useState("");
  const [announcement, setAnnouncement] = useState("World view ready");
  const [hubBusy, setHubBusy] = useState(false);
  const [hubError, setHubError] = useState("");
  // A narrow notice must never erase an outstanding scope-wide refresh. Sequence fences
  // also keep notices received during a read from being acknowledged by that older read.
  const [pendingChange, setPendingChange] = useState<string | null>(null);
  const changeSequence = useRef(0);
  const [bootstrapGeneration, setBootstrapGeneration] = useState(0);
  const sectionAbort = useRef<AbortController | null>(null);
  const deferredAbort = useRef<AbortController | null>(null);
  const contextAbort = useRef<AbortController | null>(null);
  const worldScopeAbort = useRef<AbortController | null>(null);
  const campaignWriteAbort = useRef<AbortController | null>(null);
  const campaignWritePending = useRef(false);
  const loadedWorldScopes = useRef(new Set<string>());
  const [deferredStates, setDeferredStates] = useState<Partial<Record<DeferredHubSection, DeferredViewState>>>({});
  const [deferredErrors, setDeferredErrors] = useState<Partial<Record<DeferredHubSection, string>>>({});
  useEffect(() => {
    const invalidate = () => {
      ++changeSequence.current;
      setPendingChange("scope");
    };
    const objectChanged = (event: Event) => {
      const qualifiedId = (event as CustomEvent).detail?.object?.qualifiedId;
      if (![CAMPAIGN_SUMMARY_OBJECT_ID, CAMPAIGN_LOCATION_VISITS_OBJECT_ID,
        WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID, FACTION_DIRECTORY_OBJECT_ID].includes(qualifiedId)) return;
      ++changeSequence.current;
      setPendingChange((current) => current === null || current === qualifiedId ? qualifiedId : "scope");
    };
    window.addEventListener("dnd2024-view-invalidated", invalidate);
    window.addEventListener("dnd2024-object-changed", objectChanged);
    return () => {
      window.removeEventListener("dnd2024-view-invalidated", invalidate);
      window.removeEventListener("dnd2024-object-changed", objectChanged);
    };
  }, []);
  const hubRequestSequence = useRef(0);

  const perspective = envelope.audience.perspective;
  const playerPreview = envelope.audience.seat === "dm" && perspective === "player";
  useEffect(() => subscribeChanges?.(envelope), [
    subscribeChanges, envelope.applicationId, envelope.stateSpaceId, perspective,
    envelope.contextSelection?.selectedCampaignId,
  ]);
  const contextSelection: HubContextSelection = envelope.contextSelection ?? {
    selectedWorldId: envelope.world.id,
    selectedCampaignId: envelope.revision,
    worlds: [{
      id: envelope.world.id,
      name: envelope.world.name,
      campaigns: [{ id: envelope.revision, name: envelope.campaign.title }],
    }],
  };
  const allLocations = envelope.world.locations as WorldLocation[];
  const visibleLocations = filterLocations(allLocations, locationQuery) as WorldLocation[];
  const currentLocation = resolveSelectedLocation(
    allLocations,
    envelope.world.currentLocationId,
    envelope.world.currentLocationId,
  ) as WorldLocation;
  const currentSceneLocation = resolveCurrentSceneLocation(
    allLocations,
    envelope.currentSituation?.status === "ready" && envelope.currentSituation.locationId
      ? envelope.currentSituation.locationId
      : envelope.world.currentLocationId,
  ) as WorldLocation | null;
  const currentSituation = envelope.currentSituation ?? (currentSceneLocation
    ? { status: "ready" as const, kind: "exploration" as const, locationId: currentSceneLocation.id }
    : { status: "unavailable" as const, message: "No authoritative current scene is available." });
  const currentSceneImage = currentSituation.status === "ready" && currentSituation.kind !== "recorded" && currentSituation.scene
    ? currentSituation.scene
    : currentSceneLocation?.media?.scene ?? currentSceneLocation?.media?.setting ?? null;
  const selectedLocation = resolveSelectedLocation(
    allLocations,
    selectedLocationId,
    envelope.world.currentLocationId,
  ) as WorldLocation;

  async function requestHub(
    nextPerspective: Perspective,
    nextCampaignId: string,
    announce = true,
    force = false,
  ) {
    const requested = normalizePerspective(nextPerspective) as Perspective;
    if (
      (!force && requested === perspective && nextCampaignId === contextSelection.selectedCampaignId) ||
      !envelope.audience.allowedPerspectives.includes(requested)
    ) return;

    const requestId = ++hubRequestSequence.current;
    const observedChange = changeSequence.current;
    sectionAbort.current?.abort();
    deferredAbort.current?.abort();
    contextAbort.current?.abort();
    campaignWriteAbort.current?.abort();
    setHubBusy(true);
    setHubError("");
    try {
      let nextEnvelope: unknown;
      if (loadEnvelope) {
        nextEnvelope = await loadEnvelope(requested, nextCampaignId, !force);
      } else {
        const parameters = new URLSearchParams({
          perspective: requested,
          campaign: nextCampaignId,
        });
        const response = await fetch(`/api/hub?${parameters.toString()}`, {
          credentials: "same-origin",
          headers: { Accept: "application/json" },
        });
        nextEnvelope = await response.json();
        if (!response.ok) {
          throw new Error("The perspective response was unavailable.");
        }
      }
      if (requestId !== hubRequestSequence.current) return;
      if (!isReadyHubEnvelope(nextEnvelope)) {
        throw new Error("The perspective response was unavailable.");
      }

      const loadedEnvelope = nextEnvelope as ReadyHubEnvelope;
      const campaignChanged = loadedEnvelope.contextSelection?.selectedCampaignId !==
        contextSelection.selectedCampaignId;
      const perspectiveChanged = loadedEnvelope.audience.perspective !== perspective;
      const readyEnvelope = campaignChanged || perspectiveChanged
        ? loadedEnvelope
        : preserveLastGoodPartyData(envelope, loadedEnvelope);
      setEnvelope(readyEnvelope);
      setDeferredStates({});
      setDeferredErrors({});
      loadedWorldScopes.current.clear();
      dispatchObjectUi({ type: "campaign-details-invalidated" });
      setBootstrapGeneration((generation) => generation + 1);
      if (campaignChanged || perspectiveChanged) {
        dispatchObjectUi({
          type: "scope-replaced",
          factionId: readyEnvelope.world.factions[0]?.id ?? "",
        });
        sectionAbort.current?.abort();
      }
      if (changeSequence.current === observedChange) setPendingChange(null);
      setLocationSection(
        normalizeLocationSection(
          locationSection,
          readyEnvelope.audience.perspective,
        ) as LocationSectionId,
      );
      if (campaignChanged || !readyEnvelope.world.locations.some((location) => location.id === selectedLocationId)) {
        setSelectedLocationId(readyEnvelope.world.currentLocationId);
      }
      if (!campaignChanged && !perspectiveChanged &&
          !readyEnvelope.world.factions.some((faction) => faction.id === selectedFactionId)) {
        dispatchObjectUi({ type: "faction-selected", factionId: readyEnvelope.world.factions[0]?.id ?? "" });
      }
      if (campaignChanged || !readyEnvelope.world.people.some((person) => person.id === selectedPersonId)) {
        setSelectedPersonId(readyEnvelope.world.people[0]?.id ?? "");
      }
      const nextMapId = normalizeMapId(
        readyEnvelope.world.maps,
        campaignChanged ? readyEnvelope.world.rootMapId : activeMapId,
        readyEnvelope.world.rootMapId,
      ) as string;
      setActiveMapId(nextMapId);
      if (
        !resolveSelectedMapFeature(
          resolveMapDocument(readyEnvelope.world.maps, nextMapId),
          selectedMapFeatureId,
        )
      ) {
        setSelectedMapFeatureId("");
      }
      saveEffectivePerspective(readyEnvelope.audience.perspective);
      if (readyEnvelope.contextSelection?.selectedCampaignId) {
        saveSelectedCampaign(readyEnvelope.contextSelection.selectedCampaignId);
      }
      if (announce) {
        setAnnouncement(campaignChanged
          ? `${readyEnvelope.campaign.title} opened in ${readyEnvelope.world.name}`
          : `${readyEnvelope.audience.perspective === "dm" ? "DM" : "Player"} perspective active`);
      }
    } catch (error) {
      if (requestId !== hubRequestSequence.current ||
          (error instanceof ViewReadError && error.category === "cancelled")) return;
      setHubError(error instanceof ViewReadError && error.category === "transport"
        ? error.message
        : "The view could not be changed. Your current information is still available.");
      setAnnouncement("World or campaign change unavailable");
    } finally {
      if (requestId === hubRequestSequence.current) setHubBusy(false);
    }
  }

  async function requestPerspective(nextPerspective: Perspective, announce = true) {
    if ((itemRoute.kind === "item" || itemRoute.kind === "inventory") && envelope.audience.allowedPerspectives.includes(nextPerspective)) {
      navigateItemRoute({ ...itemRoute, perspective: nextPerspective }, false, null);
      return;
    }
    await requestHub(nextPerspective, contextSelection.selectedCampaignId, announce);
  }

  async function requestCampaign(nextCampaignId: string) {
    if (itemRoute.kind !== "none") navigateItemRoute(null, true);
    await requestHub(perspective, nextCampaignId, true);
  }

  useEffect(() => {
    if (parseItemRoute(window.location.hash).kind !== "none") return;
    const storedPerspective = loadRequestedPerspective();
    const storedCampaign = loadRequestedCampaignId();
    const requestedCampaign = storedCampaign && contextSelection.worlds.some((world) =>
      world.campaigns.some((campaign) => campaign.id === storedCampaign))
      ? storedCampaign
      : contextSelection.selectedCampaignId;
    const requestedPerspective = storedPerspective &&
      envelope.audience.allowedPerspectives.includes(storedPerspective)
      ? storedPerspective
      : initialEnvelope.audience.perspective;
    if (
      requestedPerspective !== initialEnvelope.audience.perspective ||
      requestedCampaign !== contextSelection.selectedCampaignId
    ) {
      void requestHub(requestedPerspective, requestedCampaign, false);
    }
    // The first preference request is intentionally evaluated once by the server.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const requestedItemScope = itemRoute.kind === "item" || itemRoute.kind === "inventory"
    ? `${itemRoute.campaignId}:${itemRoute.perspective}` : itemRoute.kind;
  useEffect(() => {
    if (itemRoute.kind === "none") return;
    ++hubRequestSequence.current;
    setHubBusy(false);
    if ((itemRoute.kind !== "item" && itemRoute.kind !== "inventory") ||
        !envelope.audience.allowedPerspectives.includes(itemRoute.perspective) ||
        !contextSelection.worlds.some((world) => world.campaigns.some((campaign) => campaign.id === itemRoute.campaignId))) return;
    void requestHub(itemRoute.perspective, itemRoute.campaignId, false);
    // A fragment requests a scope through the existing authorized loader; it
    // never changes the host seat or supplies authoritative character bindings.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestedItemScope]);

  function focusViewHeading() {
    window.requestAnimationFrame(() => document.querySelector<HTMLElement>("#main-view-heading")?.focus());
  }

  async function requestDeferred(section: DeferredHubSection, force = false) {
    if (!loadDeferredSection || (!force && deferredStates[section] === "ready")) return;
    const owner = section === "context" ? contextAbort : deferredAbort;
    owner.current?.abort();
    const controller = new AbortController();
    owner.current = controller;
    setDeferredStates((states) => ({ ...states, [section]: "loading" }));
    setDeferredErrors((errors) => ({ ...errors, [section]: "" }));
    try {
      const loaded = await loadDeferredSection(envelope, section, controller.signal);
      if (controller.signal.aborted) return;
      setEnvelope((current) => applyDeferredHubUpdate(current, loaded));
      if (section === "locations") loadedWorldScopes.current.add(
        envelope.contextSelection?.selectedWorldId ?? envelope.world.id,
      );
      setDeferredStates((states) => ({ ...states, [section]: "ready" }));
    } catch (error) {
      if (controller.signal.aborted) return;
      setDeferredStates((states) => ({ ...states, [section]: "error" }));
      setDeferredErrors((errors) => ({ ...errors,
        [section]: error instanceof Error ? error.message : "The view is unavailable." }));
    }
  }

  async function requestWorldScope(scopeId: string) {
    if (!loadWorldScope || loadedWorldScopes.current.has(scopeId)) return;
    worldScopeAbort.current?.abort();
    const controller = new AbortController();
    worldScopeAbort.current = controller;
    try {
      const loaded = await loadWorldScope(envelope, scopeId, controller.signal);
      if (controller.signal.aborted) return;
      loadedWorldScopes.current.add(scopeId);
      setEnvelope((current) => applyDeferredHubUpdate(current, loaded));
    } catch (error) {
      if (controller.signal.aborted) return;
      setHubError(error instanceof Error ? error.message : "The map scope is unavailable.");
    }
  }

  const deferredSection: DeferredHubSection | null = activeTab === "current" ? "current"
    : activeTab === "world" && worldSection === "factions" && perspective !== "dm" ? "lore"
    : activeTab === "world" && ["map", "locations", "history", "lore", "people"].includes(worldSection)
      ? worldSection === "map" ? "locations" : worldSection as DeferredHubSection : null;
  const deferredRestricted = Boolean(loadDeferredSection && playerPreview &&
    (deferredSection === "lore" || deferredSection === "people"));
  const deferredState = deferredSection && loadDeferredSection
    ? deferredStates[deferredSection] ?? "unloaded" : "ready";
  useEffect(() => {
    if (deferredState === "ready" && !hubBusy) markActiveViewReady(activeTab);
  }, [activeTab, deferredState, hubBusy]);
  useEffect(() => {
    if (deferredSection && !hubBusy && !deferredRestricted) void requestDeferred(deferredSection);
    return () => { deferredAbort.current?.abort(); };
    // Loads belong to the selected view and the newly authorized bootstrap, not to every
    // incremental envelope merge. Errors retry only through the explicit retry button.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deferredSection, perspective, contextSelection.selectedCampaignId, hubBusy, loadDeferredSection, deferredRestricted]);
  useEffect(() => () => {
    sectionAbort.current?.abort(); deferredAbort.current?.abort(); contextAbort.current?.abort();
    worldScopeAbort.current?.abort(); campaignWriteAbort.current?.abort();
  }, []);

  const deferredNotice = deferredRestricted ? <ActorBindingRequired /> : deferredState !== "ready" ? (
    <section aria-busy={deferredState === "loading" || deferredState === "unloaded"}
      className="view-loading" role={deferredState === "error" ? "alert" : "status"}>
      <h1 id="main-view-heading" tabIndex={-1}>
        {deferredState === "error" ? "View unavailable" : `Opening ${deferredSection}`}
      </h1>
      <p>{deferredState === "error" && deferredSection
        ? deferredErrors[deferredSection] : "Loading the complete authorized view."}</p>
      {deferredState === "error" && deferredSection
        ? <button type="button" onClick={() => void requestDeferred(deferredSection, true)}>Retry view</button> : null}
    </section>
  ) : null;

  async function requestFactionPage(cursor: string | null) {
    if (!loadFactionPage || perspective !== "dm" || hubBusy) return;
    sectionAbort.current?.abort();
    const controller = new AbortController();
    const observedChange = changeSequence.current;
    sectionAbort.current = controller;
    setHubBusy(true);
    setHubError("");
    try {
      const page = await loadFactionPage(envelope, cursor, controller.signal);
      if (controller.signal.aborted) return;
      setEnvelope((current) => {
        const merged = cursor === null ? page.factions : [
          ...current.world.factions,
          ...page.factions.filter((item) => !current.world.factions.some((value) => value.id === item.id)),
        ];
        return { ...current, world: { ...current.world, factions: merged, factionDirectory: {
          totalCount: page.totalCount,
          complete: page.complete,
          nextCursor: page.nextCursor,
          sourceRevisionFingerprint: page.sourceRevisionFingerprint,
        } } };
      });
      if (!selectedFactionId) {
        dispatchObjectUi({ type: "faction-selected", factionId: page.factions[0]?.id ?? "" });
      }
      if (cursor === null && changeSequence.current === observedChange)
        setPendingChange((current) => current === FACTION_DIRECTORY_OBJECT_ID ? null : current);
    } catch (error) {
      if (!controller.signal.aborted) setHubError(error instanceof Error ? error.message : "The faction directory is unavailable.");
    } finally {
      if (!controller.signal.aborted) setHubBusy(false);
    }
  }

  async function requestCampaignDetails() {
    if (!loadCampaignDetails || campaignDetailsLoaded || hubBusy) return;
    sectionAbort.current?.abort();
    const controller = new AbortController();
    sectionAbort.current = controller;
    setHubBusy(true);
    setHubError("");
    try {
      const campaign = await loadCampaignDetails(envelope, controller.signal);
      if (controller.signal.aborted) return;
      setEnvelope((current) => ({ ...current, campaign }));
      dispatchObjectUi({ type: "campaign-details-loaded" });
    } catch (error) {
      if (!controller.signal.aborted) setHubError(error instanceof Error ? error.message : "The campaign details are unavailable.");
    } finally {
      if (!controller.signal.aborted) setHubBusy(false);
    }
  }

  useEffect(() => {
    if (bootstrapGeneration === 0) return;
    // A refresh returns a minimal bootstrap even when the selected view is unchanged.
    // Rehydrate that view once; failed reads remain explicit and do not start a retry loop.
    if (activeTab === "campaign" && campaignSection !== "overview") void requestCampaignDetails();
    if (activeTab === "world" && worldSection === "factions" && !envelope.world.factionDirectory)
      void requestFactionPage(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bootstrapGeneration]);

  useEffect(() => {
    if (activeTab === "campaign" && campaignSection !== "overview") void requestCampaignDetails();
    // Navigation, including Back/Forward, owns lazy Campaign detail activation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, campaignSection]);

  function selectTab(tab: MainTabId) {
    const nextTab = normalizeMainTab(tab) as MainTabId;
    if (nextTab === activeTab) return;
    navigateHubRoute(nextTab, campaignSection);
    setActiveTab(nextTab);
    if (nextTab === "world" && worldSection === "factions" && !envelope.world.factionDirectory)
      void requestFactionPage(null);
    setAnnouncement(`${nextTab === "current" ? "Current view" : nextTab} opened`);
    focusViewHeading();
  }

  function selectWorldSection(section: WorldSectionId) {
    const nextSection = normalizeWorldSection(section) as WorldSectionId;
    setWorldSection(nextSection);
    if (nextSection === "factions" && !envelope.world.factionDirectory) void requestFactionPage(null);
    setAnnouncement(`World ${nextSection} opened`);
    focusViewHeading();
  }

  function selectCampaignSection(section: CampaignSectionId) {
    const nextSection = normalizeCampaignSection(section) as CampaignSectionId;
    if (nextSection === campaignSection) return;
    navigateHubRoute("campaign", nextSection);
    setCampaignSection(nextSection);
    setAnnouncement(`Campaign ${nextSection === "log" ? "adventure log" : nextSection} opened`);
    focusViewHeading();
  }

  function openCampaignLocation(locationId: string) {
    const location = resolveCampaignWorldTarget(allLocations, locationId);
    if (!location) return;
    setSelectedLocationId(locationId);
    setLocationSection("details");
    setWorldSection("locations");
    navigateHubRoute("world");
    setActiveTab("world");
    setAnnouncement(`${location.name} opened from Campaign`);
    focusViewHeading();
  }

  function refreshChangedView() {
    if (pendingChange === FACTION_DIRECTORY_OBJECT_ID) {
      void requestFactionPage(null);
      return;
    }
    void requestHub(perspective, contextSelection.selectedCampaignId, false, true);
  }

  async function saveCampaignPremise(premise: string) {
    if (!writeCampaignPremise || campaignWritePending.current ||
        envelope.audience.seat !== "dm" || perspective !== "dm") return;
    campaignWritePending.current = true;
    campaignWriteAbort.current?.abort();
    const controller = new AbortController();
    campaignWriteAbort.current = controller;
    dispatchObjectUi({ type: "write-submitted", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
    try {
      const result = await writeCampaignPremise({ envelope, premise }, controller.signal);
      if (controller.signal.aborted) return;
      campaignWriteAbort.current = null;
      setEnvelope((current) => ({
        ...current,
        campaign: { ...current.campaign, premise: result.premise },
        objectQueries: {},
      }));
      dispatchObjectUi({ type: "write-confirmed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
      setAnnouncement(result.replayed
        ? "Campaign premise confirmed after retry"
        : result.noOp ? "Campaign premise already matched the saved campaign" : "Campaign premise saved");
      await requestHub(perspective, contextSelection.selectedCampaignId, false, true);
    } catch (error) {
      if (controller.signal.aborted) return;
      const message = error instanceof Error ? error.message : "The campaign premise was not changed.";
      dispatchObjectUi({ type: "write-failed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID, error: message });
      setAnnouncement("Campaign premise was not changed");
      const category = error && typeof error === "object" && "category" in error
        ? String(error.category) : "";
      if (category === "stale")
        await requestHub(perspective, contextSelection.selectedCampaignId, false, true);
    } finally {
      if (campaignWriteAbort.current === controller) campaignWriteAbort.current = null;
      campaignWritePending.current = false;
    }
  }

  function focusWorldEntityCard(kind: "person" | "faction", entityId: string) {
    window.requestAnimationFrame(() => document.getElementById(`world-${kind}-${entityId}`)?.focus());
  }

  function openCampaignPerson(personId: string) {
    const person = resolveCampaignWorldTarget(envelope.world.people, personId);
    if (!person) return;
    setSelectedPersonId(personId);
    setWorldSection("people");
    setActiveTab("world");
    setAnnouncement(`${person.name} opened from Campaign`);
    focusWorldEntityCard("person", personId);
  }

  function openCampaignFaction(factionId: string) {
    const faction = resolveCampaignWorldTarget(envelope.world.factions, factionId);
    if (!faction) return;
    dispatchObjectUi({ type: "faction-selected", factionId });
    setWorldSection("factions");
    setActiveTab("world");
    setAnnouncement(`${faction.name} opened from Campaign`);
    focusWorldEntityCard("faction", factionId);
  }

  function renderActiveView() {
    switch (activeTab) {
      case "campaign":
        return (
          <CampaignView
            campaign={envelope.campaign}
            premiseEdit={objectUi.edits[CAMPAIGN_SUMMARY_OBJECT_ID]}
            onOpenFaction={openCampaignFaction}
            onOpenLocation={openCampaignLocation}
            onOpenPerson={openCampaignPerson}
            onBeginPremiseEdit={writeCampaignPremise && envelope.audience.seat === "dm" && perspective === "dm" &&
              envelope.objectQueries?.campaignSummary ? () => dispatchObjectUi({
                type: "edit-staged", objectId: CAMPAIGN_SUMMARY_OBJECT_ID,
                draft: { premise: envelope.campaign.premise },
              }) : undefined}
            onCancelPremiseEdit={writeCampaignPremise ? () => dispatchObjectUi({
              type: "edit-cancelled", objectId: CAMPAIGN_SUMMARY_OBJECT_ID,
            }) : undefined}
            onPremiseDraftChange={writeCampaignPremise ? (premise) => dispatchObjectUi({
              type: "edit-staged", objectId: CAMPAIGN_SUMMARY_OBJECT_ID, draft: { premise },
            }) : undefined}
            onSavePremise={writeCampaignPremise ? (premise) => void saveCampaignPremise(premise) : undefined}
            onSectionChange={selectCampaignSection}
            section={campaignSection}
            worldName={envelope.world.name}
          />
        );
      case "party":
        if (loadDeferredSection && playerPreview && itemRoute.kind !== "none") return <ActorBindingRequired />;
        if (itemRoute.kind === "none" || itemRoute.kind === "inventory" &&
            itemRoute.campaignId === contextSelection.selectedCampaignId &&
            itemRoute.perspective === perspective &&
            envelope.party.some((member) => member.id === itemRoute.characterId)) {
          const selectedId = itemRoute.kind === "inventory" ? itemRoute.characterId : undefined;
          const inventoryReturn = selectedId ? readInventoryReturn(window.history.state, selectedId) : null;
          return <CharacterWorkspace
            key={`${envelope.applicationId}:${envelope.stateSpaceId}:${contextSelection.selectedCampaignId}:${envelope.audience.seat}:${perspective}:${selectedId ?? "party"}`}
            navigationCharacterId={selectedId}
            inventoryReturn={inventoryReturn}
            loadCharacterSheet={loadCharacterSheet ? readCharacterSheet : undefined}
            loadCharacterDetails={loadCharacterDetails ? readCharacterDetails : undefined}
            loadCharacterInventory={loadCharacterInventory ? readCharacterInventory : undefined}
            loading={hubBusy}
            onRetry={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}
            onOpenItem={(characterId, itemId, context) => {
              const inventory = { kind: "inventory" as const, characterId,
                campaignId: contextSelection.selectedCampaignId, perspective };
              navigateItemRoute(inventory, true, context);
              navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
            }}
            party={envelope.party}
            summaryOnly={Boolean(loadDeferredSection && playerPreview)}
          />;
        }
        return <ItemWorkspace
          key={`${envelope.applicationId}:${envelope.stateSpaceId}:${contextSelection.selectedCampaignId}:${envelope.audience.seat}:${perspective}`}
          route={itemRoute}
          context={envelope}
          campaignId={contextSelection.selectedCampaignId}
          perspective={perspective}
          itemClient={itemClientCache.current?.scope === itemClientScope ? itemClientCache.current.client : undefined}
          retainItemClient={retainItemClient}
          loadCharacterSheet={loadCharacterSheet ? readCharacterSheet : undefined}
          loadCharacterDetails={loadCharacterDetails ? readCharacterDetails : undefined}
          loadCharacterInventory={loadCharacterInventory ? readCharacterInventory : undefined}
          loading={hubBusy}
          onRetry={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}
          party={envelope.party}
        />;
      case "current":
        if (deferredNotice) return deferredNotice;
        return (
          <div className="current-play-workspace" data-view-status={currentSituation.status}
            data-record-id={currentSituation.status === "ready"
              ? `${currentSituation.kind}:${currentSituation.locationId ?? "unplaced"}` : undefined}>
            <CurrentViewPreview
              image={currentSceneImage}
              location={currentSceneLocation}
              situation={currentSituation}
              perspective={perspective}
              draftScope={envelope.audience.seat === "dm" && perspective === "dm" ? {
                applicationId: envelope.applicationId, stateSpaceId: envelope.stateSpaceId, campaignId: contextSelection.selectedCampaignId,
              } : undefined}
              onBoardAccepted={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}
            />
            <PlayConversationPanel
              applicationId={envelope.applicationId}
              stateSpaceId={envelope.stateSpaceId}
              sessionContextId={contextSelection.selectedCampaignId}
              onConversationChange={() => {
                void requestHub(perspective, contextSelection.selectedCampaignId, false, true);
              }}
            />
          </div>
        );
      case "rules":
        return <RulesView loadRules={loadRules} rules={envelope.rules} />;
      case "content":
        return <InstalledContentView loadContent={loadContent} />;
      case "world":
      default:
        return (
          <WorldView
            directoriesDeferred={Boolean(loadDeferredSection)}
            deferredNotice={deferredNotice}
            campaign={envelope.campaign}
            currentLocation={currentLocation}
            filteredLocations={visibleLocations}
            locationSection={locationSection}
            perspective={perspective}
            selectedFactionId={selectedFactionId}
            selectedPersonId={selectedPersonId}
            onLocationSelect={(locationId) => {
              setSelectedLocationId(locationId);
              setAnnouncement(
                `${allLocations.find((location) => location.id === locationId)?.name ?? "Location"} selected`,
              );
            }}
            onQueryChange={(query) => setLocationQuery(query.slice(0, 80))}
            onLocationSectionChange={(section) => {
              const nextSection = normalizeLocationSection(section, perspective) as LocationSectionId;
              setLocationSection(nextSection);
              setAnnouncement(`${selectedLocation.name} ${nextSection} opened`);
            }}
            onFactionSelect={(factionId) => {
              dispatchObjectUi({ type: "faction-selected", factionId });
              setAnnouncement(
                `${envelope.world.factions.find((faction) => faction.id === factionId)?.name ?? "Faction"} selected`,
              );
            }}
            factionDirectoryBusy={hubBusy && worldSection === "factions"}
            onLoadMoreFactions={() => void requestFactionPage(envelope.world.factionDirectory?.nextCursor ?? null)}
            activeMapId={activeMapId}
            onMapChange={(mapId) => {
              const nextMapId = normalizeMapId(
                envelope.world.maps,
                mapId,
                envelope.world.rootMapId,
              ) as string;
              if (nextMapId === activeMapId) return;
              setActiveMapId(nextMapId);
              setSelectedMapFeatureId("");
              const map = resolveMapDocument(envelope.world.maps, nextMapId);
              if (map) void requestWorldScope(map.subject.id);
              setAnnouncement(
                `${resolveMapDocument(envelope.world.maps, nextMapId)?.subject.name ?? "Map"} map opened`,
              );
            }}
            onMapFeatureSelect={(featureId) => {
              const map = resolveMapDocument(envelope.world.maps, activeMapId);
              const feature = resolveSelectedMapFeature(map, featureId);
              setSelectedMapFeatureId(feature ? feature.id : "");
              setAnnouncement(feature ? `${feature.name} selected` : "Map selection cleared");
            }}
            onMapNavigateToFeature={(mapId, featureId) => {
              const nextMapId = normalizeMapId(
                envelope.world.maps,
                mapId,
                envelope.world.rootMapId,
              ) as string;
              const map = resolveMapDocument(envelope.world.maps, nextMapId);
              const feature = resolveSelectedMapFeature(map, featureId);
              setActiveMapId(nextMapId);
              setSelectedMapFeatureId(feature?.id ?? "");
              if (map) void requestWorldScope(map.subject.id);
              setAnnouncement(feature
                ? `${feature.name} opened on ${map?.subject.name ?? "the map"}`
                : `${map?.subject.name ?? "Map"} opened`);
            }}
            onSectionChange={selectWorldSection}
            query={locationQuery}
            section={worldSection}
            selectedMapFeatureId={selectedMapFeatureId}
            selectedLocation={selectedLocation}
            world={envelope.world}
          />
        );
    }
  }

  return (
    <div className="information-hub" data-perspective={perspective}>
      <a className="skip-link" href="#information-content">Skip to information</a>
      <TopBar
        allowedPerspectives={envelope.audience.allowedPerspectives}
        busy={hubBusy}
        contextSelection={contextSelection}
        onCampaignChange={(campaignId) => void requestCampaign(campaignId)}
        perspective={perspective}
        onPerspectiveChange={(nextPerspective) => void requestPerspective(nextPerspective)}
        onOpenContext={loadDeferredSection ? () => void requestDeferred("context") : undefined}
        contextState={deferredStates.context}
        contextError={deferredErrors.context}
      />
      {hubError ? <p className="perspective-notice" role="alert">{hubError}</p> : null}
      {pendingChange !== null ? <div className="perspective-notice" role="status">
        The server changed or the live connection was interrupted. Showing the last loaded view.
        <button type="button" disabled={hubBusy} onClick={refreshChangedView}>Refresh view</button>
      </div> : null}
      <div className="information-hub__body">
        <MainNavigation
          activeTab={activeTab}
          chapter={envelope.campaign.chapter}
          onSelect={selectTab}
        />
        <main className="information-content" id="information-content">
          <ViewErrorBoundary key={activeTab} viewLabel={activeTab === "current" ? "Current view" : activeTab}>
            <Suspense fallback={<ViewLoading label={activeTab === "current" ? "Current view" : activeTab} />}>
              {renderActiveView()}
            </Suspense>
          </ViewErrorBoundary>
        </main>
      </div>
      <div aria-atomic="true" aria-live="polite" className="sr-only">{announcement}</div>
    </div>
  );
}
