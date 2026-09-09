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
  PartySectionId,
  Perspective,
  ReadyHubEnvelope,
  RulesReferencePublication,
  WorldLocation,
  WorldSectionId,
} from "../data/hub-types";
import {
  filterLocations,
  isReadyHubEnvelope,
  normalizeCampaignSection,
  normalizeMainTab,
  normalizeLocationSection,
  normalizePartySection,
  normalizePerspective,
  normalizeWorldSection,
  normalizeMapId,
  resolveCurrentSceneLocation,
  resolveMapDocument,
  resolveSelectedMapFeature,
} from "../state.js";
import { MainNavigation } from "./MainNavigation";
import type { InstalledContentLoader } from "./InstalledContentView";
import type { ItemViewClient } from "../server/item-view-client";
import type { ItemDefinitionLoader, ItemRegistryPageLoader } from "./registry/ItemRegistryWorkspace";
import type { RecipeDefinitionLoader, RecipeRegistryPageLoader } from "./registry/RecipeRegistryWorkspace";
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
const ItemRegistryWorkspace = lazy(() => import("./registry/ItemRegistryWorkspaceFeature")
  .then((module) => ({ default: module.ItemRegistryWorkspace })));
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

function ObserverPreviewUnavailable() {
  return <section className="view-unavailable" role="status"
    data-view-status="unavailable" data-reason-code="audience-restricted">
    <h1 id="main-view-heading" tabIndex={-1}>Unavailable in observer preview</h1>
    <p>This optional preview omits private table information. Return to the shared table to open the complete view.</p>
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

type RulesLoader = (preferCached?: boolean, signal?: AbortSignal) => Promise<RulesReferencePublication>;
type FactionPageLoader = (
  envelope: ReadyHubEnvelope,
  cursor: string | null,
  signal: AbortSignal,
) => Promise<FactionDirectoryPage>;
type CampaignDetailsLoader = (envelope: ReadyHubEnvelope, signal: AbortSignal) => Promise<CampaignReadModel>;
type CampaignDetailsViewState = {
  status: "unloaded" | "loading" | "ready" | "error";
  data: CampaignReadModel | null;
  error: string;
};

export function DndInformationHub({
  initialEnvelope,
  loadEnvelope,
  loadRules,
  loadContent,
  loadCharacterSheet,
  loadCharacterDetails,
  loadCharacterInventory,
  loadInventoryContainer,
  loadItemRegistryPage,
  loadItemDefinition,
  loadRecipeRegistryPage,
  loadRecipeDefinition,
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
  loadContent: InstalledContentLoader;
  loadCharacterSheet?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").PartyMemberReadModel>;
  loadCharacterDetails?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").PartyMemberReadModel>;
  loadCharacterInventory?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").InventoryContainerResult>;
  loadInventoryContainer?: (envelope: ReadyHubEnvelope, actorId: string, containerId: string, signal: AbortSignal) => Promise<import("../data/hub-types").InventoryContainerPageResult>;
  loadItemRegistryPage?: ItemRegistryPageLoader;
  loadItemDefinition?: ItemDefinitionLoader;
  loadRecipeRegistryPage?: RecipeRegistryPageLoader;
  loadRecipeDefinition?: RecipeDefinitionLoader;
  loadFactionPage?: FactionPageLoader;
  loadCampaignDetails?: CampaignDetailsLoader;
  loadDeferredSection?: (envelope: ReadyHubEnvelope, section: DeferredHubSection, signal: AbortSignal) => Promise<DeferredHubUpdate>;
  loadWorldScope?: (envelope: ReadyHubEnvelope, scopeId: string, cursor: string | null, signal: AbortSignal) => Promise<Extract<DeferredHubUpdate, { section: "locations" }>>;
  writeCampaignPremise?: CampaignPremiseWriter;
  subscribeChanges?: (envelope: ReadyHubEnvelope) => () => void;
}) {
  const [envelope, setEnvelope] = useState(initialEnvelope);
  const [bootstrapGeneration, setBootstrapGeneration] = useState(0);
  const itemClientScope = `${envelope.applicationId}:${envelope.stateSpaceId}:${bootstrapGeneration}:${envelope.audience.seat}:${envelope.audience.perspective}`;
  const itemClientScopeRef = useRef(itemClientScope);
  itemClientScopeRef.current = itemClientScope;
  const itemClientCache = useRef<{ scope: string; client: ItemViewClient } | null>(null);
  const retainItemClient = useCallback((client: ItemViewClient) => {
    const previous = itemClientCache.current;
    if (previous && previous.client !== client) previous.client.invalidate("workspace-replaced");
    itemClientCache.current = { scope: itemClientScopeRef.current, client };
  }, []);
  useEffect(() => () => itemClientCache.current?.client.invalidate("scope-replaced"), []);
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
  const readInventoryContainer = useCallback((actorId: string, containerId: string, signal: AbortSignal) => {
    if (!loadInventoryContainer) throw new Error("Inventory container loading is unavailable.");
    return loadInventoryContainer(envelope, actorId, containerId, signal);
  }, [envelope, loadInventoryContainer]);
  const [itemRoute, setItemRoute] = useState(() => parseItemRoute(window.location.hash));
  const [activeTab, setActiveTab] = useState<MainTabId>(() => {
    const item = parseItemRoute(window.location.hash);
    if (item.kind !== "none")
      return normalizeMainTab(window.history.state?.itemMainTab ?? "party") as MainTabId;
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.tab : "world";
  });
  const [campaignSection, setCampaignSection] = useState<CampaignSectionId>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.campaignSection : "overview";
  });
  const [partySection, setPartySection] = useState<PartySectionId>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.partySection : "overview";
  });
  const [partyCharacterId, setPartyCharacterId] = useState<string | null>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.characterId : null;
  });
  useEffect(() => {
    const changed = () => {
      const next = parseItemRoute(window.location.hash);
      setItemRoute(next);
      if (next.kind !== "none")
        setActiveTab(normalizeMainTab(window.history.state?.itemMainTab ?? "party") as MainTabId);
      else {
        const hub = parseHubRoute(window.location.hash);
        if (hub.kind === "hub") {
          setActiveTab(hub.tab);
          setCampaignSection(hub.campaignSection);
          setPartySection(hub.partySection);
          setPartyCharacterId(hub.characterId);
          setWorldSection(hub.worldSection);
          setLocationScopePath(hub.locationScopePath);
          if (hub.locationId) setSelectedLocationId(hub.locationId);
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
  const [worldSection, setWorldSection] = useState<WorldSectionId>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.worldSection : "overview";
  });
  const [locationSection, setLocationSection] = useState<LocationSectionId>("details");
  const [selectedLocationId, setSelectedLocationId] = useState(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.locationId ?? initialEnvelope.world.currentLocationId
      : initialEnvelope.world.currentLocationId;
  });
  const [locationScopePath, setLocationScopePath] = useState<string[]>(() => {
    const route = parseHubRoute(window.location.hash);
    return route.kind === "hub" ? route.locationScopePath : [];
  });
  const [objectUi, dispatchObjectUi] = useReducer(
    hubObjectUiReducer,
    initialEnvelope.world.factions[0]?.id ?? "",
    createHubObjectUiState,
  );
  const { selectedFactionId } = objectUi;
  const [campaignDetails, setCampaignDetails] = useState<CampaignDetailsViewState>(() =>
    loadCampaignDetails
      ? { status: "unloaded", data: null, error: "" }
      : { status: "ready", data: initialEnvelope.campaign, error: "" });
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
  const sectionAbort = useRef<AbortController | null>(null);
  const campaignDetailsAbort = useRef<AbortController | null>(null);
  const deferredAbort = useRef<AbortController | null>(null);
  const contextAbort = useRef<AbortController | null>(null);
  const worldScopeAbort = useRef<AbortController | null>(null);
  const campaignWriteAbort = useRef<AbortController | null>(null);
  const campaignWritePending = useRef(false);
  const loadedWorldScopes = useRef(new Set<string>());
  const loadingWorldScopes = useRef(new Set<string>());
  const failedWorldScopes = useRef(new Set<string>());
  const [locationScopeBusy, setLocationScopeBusy] = useState(false);
  const [locationScopeError, setLocationScopeError] = useState("");
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
  const selectedPartyCharacterId = envelope.party.some((member) => member.id === partyCharacterId)
    ? partyCharacterId
    : envelope.party[0]?.id ?? null;
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
  const worldRootId = contextSelection.selectedWorldId;
  const activeLocationScopeId = locationScopePath.at(-1) ?? worldRootId;
  const activeLocationScope = envelope.world.locationScopes.find((scope) => scope.id === activeLocationScopeId)
    ?? null;
  const locationById = new Map(allLocations.map((location) => [location.id, location]));
  const scopedLocations = (activeLocationScope?.childIds ?? []).flatMap((id) => {
    const location = locationById.get(id);
    return location ? [location] : [];
  });
  const visibleLocations = filterLocations(scopedLocations, locationQuery) as WorldLocation[];
  const currentLocation = resolveCurrentSceneLocation(
    allLocations,
    envelope.world.currentLocationId,
  ) as WorldLocation | null;
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
  const selectedLocation = locationById.get(selectedLocationId) ?? null;
  const effectiveActiveMapId = normalizeMapId(
    envelope.world.maps,
    activeMapId,
    envelope.world.rootMapId,
  ) as string;
  const activeMapDocument = resolveMapDocument(envelope.world.maps, effectiveActiveMapId);
  const activeMapScopeId = activeMapDocument?.subject.id ?? null;
  const activeMapScopeReady = !loadWorldScope || activeMapScopeId === null ||
    envelope.world.mapOwnerId === null ||
    envelope.world.locationScopes.some((scope) => scope.id === activeMapScopeId);
  const activeMapScopeFailed = activeMapScopeId !== null && failedWorldScopes.current.has(activeMapScopeId);
  const mapScopeState: "loading" | "ready" | "error" = activeMapScopeReady
    ? "ready"
    : activeMapScopeFailed ? "error" : "loading";
  useEffect(() => {
    if (effectiveActiveMapId === activeMapId) return;
    setActiveMapId(effectiveActiveMapId);
    setSelectedMapFeatureId("");
  }, [activeMapId, effectiveActiveMapId]);

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
    ) return false;

    const requestId = ++hubRequestSequence.current;
    const observedChange = changeSequence.current;
    sectionAbort.current?.abort();
    campaignDetailsAbort.current?.abort();
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
      if (requestId !== hubRequestSequence.current) return false;
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
      setCampaignDetails((current) => !loadCampaignDetails
        ? { status: "ready", data: readyEnvelope.campaign, error: "" }
        : campaignChanged || perspectiveChanged
          ? { status: "unloaded", data: null, error: "" }
          : { status: "unloaded", data: current.data, error: "" });
      setDeferredStates({});
      setDeferredErrors({});
      loadedWorldScopes.current.clear();
      failedWorldScopes.current.clear();
      setLocationScopeError("");
      setBootstrapGeneration((generation) => generation + 1);
      if (campaignChanged || perspectiveChanged) {
        setLocationScopePath([]);
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
      if (campaignChanged || perspectiveChanged) {
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
      return true;
    } catch (error) {
      if (requestId !== hubRequestSequence.current ||
          (error instanceof ViewReadError && error.category === "cancelled")) return false;
      setHubError(error instanceof ViewReadError && error.category === "transport"
        ? error.message
        : "The view could not be changed. Your current information is still available.");
      setAnnouncement("World or campaign change unavailable");
      return false;
    } finally {
      if (requestId === hubRequestSequence.current) setHubBusy(false);
    }
  }

  async function requestPerspective(nextPerspective: Perspective, announce = true) {
    if ((itemRoute.kind === "item" || itemRoute.kind === "inventory" || itemRoute.kind === "registry-item"
        || itemRoute.kind === "registry-recipe")
        && envelope.audience.allowedPerspectives.includes(nextPerspective)) {
      navigateItemRoute({ ...itemRoute, perspective: nextPerspective }, false, null, activeTab);
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

  const requestedItemScope = itemRoute.kind === "item" || itemRoute.kind === "inventory" || itemRoute.kind === "registry-item"
    || itemRoute.kind === "registry-recipe"
    ? `${itemRoute.campaignId}:${itemRoute.perspective}` : itemRoute.kind;
  useEffect(() => {
    if (itemRoute.kind === "none") return;
    ++hubRequestSequence.current;
    setHubBusy(false);
    if ((itemRoute.kind === "item" || itemRoute.kind === "inventory" || itemRoute.kind === "registry-item"
        || itemRoute.kind === "registry-recipe") &&
        envelope.audience.seat === "dm" && envelope.audience.allowedPerspectives.length === 1 &&
        perspective === "dm" && itemRoute.perspective === "player") {
      navigateItemRoute({ ...itemRoute, perspective: "dm" }, true);
      return;
    }
    if ((itemRoute.kind !== "item" && itemRoute.kind !== "inventory" && itemRoute.kind !== "registry-item"
        && itemRoute.kind !== "registry-recipe") ||
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

  async function requestWorldScope(scopeId: string, cursor: string | null = null, force = false) {
    if (!loadWorldScope || cursor === null && !force && loadedWorldScopes.current.has(scopeId)) return true;
    if (loadingWorldScopes.current.has(scopeId)) return true;
    if (force) failedWorldScopes.current.delete(scopeId);
    worldScopeAbort.current?.abort();
    const controller = new AbortController();
    worldScopeAbort.current = controller;
    loadingWorldScopes.current.add(scopeId);
    setLocationScopeBusy(true);
    setLocationScopeError("");
    try {
      const loaded = await loadWorldScope(envelope, scopeId, cursor, controller.signal);
      if (controller.signal.aborted) return false;
      loadedWorldScopes.current.add(scopeId);
      failedWorldScopes.current.delete(scopeId);
      setEnvelope((current) => applyDeferredHubUpdate(current, loaded));
      return true;
    } catch (error) {
      if (controller.signal.aborted) return false;
      failedWorldScopes.current.add(scopeId);
      setLocationScopeError(error instanceof Error ? error.message : "This location level is unavailable.");
      return false;
    } finally {
      loadingWorldScopes.current.delete(scopeId);
      if (worldScopeAbort.current === controller) setLocationScopeBusy(false);
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
    const campaignReady = activeTab !== "campaign" || campaignDetails.status === "ready";
    const mapReady = activeTab !== "world" || worldSection !== "map" || mapScopeState !== "loading";
    if (deferredState === "ready" && campaignReady && mapReady && !hubBusy) markActiveViewReady(activeTab);
  }, [activeTab, campaignDetails.status, deferredState, hubBusy, mapScopeState, worldSection]);
  useEffect(() => {
    if (deferredSection && !hubBusy && !deferredRestricted) void requestDeferred(deferredSection);
    return () => { deferredAbort.current?.abort(); };
    // Loads belong to the selected view and the newly authorized bootstrap, not to every
    // incremental envelope merge. Errors retry only through the explicit retry button.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deferredSection, perspective, contextSelection.selectedCampaignId, hubBusy, loadDeferredSection, deferredRestricted]);
  useEffect(() => {
    if (activeTab !== "world" || worldSection !== "map" || deferredState !== "ready" ||
        mapScopeState !== "loading" || !activeMapScopeId ||
        loadingWorldScopes.current.has(activeMapScopeId)) return;
    void requestWorldScope(activeMapScopeId);
    // The active map's direct location scope supplies its markers and child-map links.
    // It is loaded only after the root location scope has identified the actual map owner.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, worldSection, deferredState, activeMapScopeId, mapScopeState, bootstrapGeneration]);
  useEffect(() => {
    if (activeTab !== "world" || worldSection !== "locations" || deferredState !== "ready" ||
        locationScopePath.length === 0) return;
    const nextScopeId = locationScopePath.find((scopeId) => !loadedWorldScopes.current.has(scopeId));
    if (nextScopeId) void requestWorldScope(nextScopeId);
    // The route path is an ordered authorization walk. Each loaded parent admits only its
    // projected child, so a deep link cannot turn an arbitrary entity id into authority.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, worldSection, deferredState, locationScopePath.join("/"), bootstrapGeneration,
    envelope.world.locationScopes.map((scope) => `${scope.id}:${scope.sourceRevisionFingerprint ?? ""}`).join("|")]);
  useEffect(() => () => {
    sectionAbort.current?.abort(); campaignDetailsAbort.current?.abort(); deferredAbort.current?.abort(); contextAbort.current?.abort();
    worldScopeAbort.current?.abort(); campaignWriteAbort.current?.abort();
  }, []);

  const deferredNotice = deferredRestricted ? <ObserverPreviewUnavailable /> : deferredState !== "ready" ? (
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
    if (!loadCampaignDetails) return;
    campaignDetailsAbort.current?.abort();
    const controller = new AbortController();
    campaignDetailsAbort.current = controller;
    setCampaignDetails((current) => ({ status: "loading", data: current.data, error: "" }));
    try {
      const campaign = await loadCampaignDetails(envelope, controller.signal);
      if (controller.signal.aborted) return;
      setEnvelope((current) => ({ ...current, campaign }));
      setCampaignDetails({ status: "ready", data: campaign, error: "" });
    } catch (error) {
      if (controller.signal.aborted) return;
      const interrupted = (error instanceof ViewReadError && error.category === "cancelled") ||
        (error instanceof DOMException && error.name === "AbortError");
      const message = interrupted
        ? "Campaign details changed while they were loading. Retry to read the current version."
        : error instanceof Error ? error.message : "The campaign details are unavailable.";
      setCampaignDetails((current) => ({ status: "error", data: current.data, error: message }));
    } finally {
      if (campaignDetailsAbort.current === controller) campaignDetailsAbort.current = null;
    }
  }

  useEffect(() => {
    if (activeTab === "campaign") void requestCampaignDetails();
    return () => { campaignDetailsAbort.current?.abort(); };
    // The selected route deliberately asks the resource owner for Campaign details. Fresh
    // cached results return immediately; expired, invalidated, and failed results stay distinct.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, bootstrapGeneration, perspective, contextSelection.selectedCampaignId]);

  useEffect(() => {
    if (bootstrapGeneration === 0) return;
    // A refresh returns a minimal bootstrap even when the selected view is unchanged.
    // Rehydrate other selected views once; failed reads remain explicit and do not start a retry loop.
    if (activeTab === "world" && worldSection === "factions" && !envelope.world.factionDirectory)
      void requestFactionPage(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bootstrapGeneration]);

  function selectTab(tab: MainTabId) {
    const nextTab = normalizeMainTab(tab) as MainTabId;
    if (nextTab === activeTab) return;
    navigateHubRoute(nextTab, campaignSection, false,
      nextTab === "world" ? {
        worldSection,
        ...(worldSection === "locations" ? { locationScopePath, locationId: selectedLocationId || null } : {}),
      } : nextTab === "party" ? {
        partySection,
        characterId: selectedPartyCharacterId,
      } : {});
    setActiveTab(nextTab);
    if (nextTab === "world" && worldSection === "factions" && !envelope.world.factionDirectory)
      void requestFactionPage(null);
    setAnnouncement(`${nextTab === "current" ? "Current view" : nextTab} opened`);
    focusViewHeading();
  }

  function selectWorldSection(section: WorldSectionId) {
    const nextSection = normalizeWorldSection(section) as WorldSectionId;
    const currentRoute = parseHubRoute(window.location.hash);
    if (!(nextSection === "locations" && currentRoute.kind === "hub" &&
        currentRoute.tab === "world" && currentRoute.worldSection === "locations")) {
      navigateHubRoute("world", "overview", false, {
        worldSection: nextSection,
        ...(nextSection === "locations" ? { locationScopePath, locationId: selectedLocationId || null } : {}),
      });
    }
    setWorldSection(nextSection);
    if (nextSection === "factions" && !envelope.world.factionDirectory) void requestFactionPage(null);
    setAnnouncement(`World ${nextSection} opened`);
    focusViewHeading();
  }

  function pathToLocation(locationId: string) {
    if (locationId === worldRootId) return [];
    const path: string[] = [];
    const visited = new Set<string>();
    let current: string | null = locationId;
    while (current && current !== worldRootId && path.length < 20 && !visited.has(current)) {
      visited.add(current);
      path.unshift(current);
      current = sourceParent(current);
    }
    return current === worldRootId ? path : [];
  }

  function sourceParent(locationId: string) {
    const scope = envelope.world.locationScopes.find((entry) => entry.id === locationId);
    if (scope?.parentId) return scope.parentId;
    const parentScope = envelope.world.locationScopes.find((entry) => entry.childIds.includes(locationId));
    return parentScope?.id ?? null;
  }

  function openLocationScope(locationId: string) {
    const derived = pathToLocation(locationId);
    const nextPath = derived.length > 0 || locationId === worldRootId
      ? derived
      : [...locationScopePath, locationId].slice(-20);
    setSelectedLocationId(locationId);
    setLocationScopePath(nextPath);
    setLocationQuery("");
    navigateHubRoute("world", "overview", false, {
      worldSection: "locations", locationScopePath: nextPath, locationId,
    });
    void requestWorldScope(locationId);
    setAnnouncement(`${locationById.get(locationId)?.name ?? "Location"} opened`);
  }

  function openParentLocationScope() {
    if (locationScopePath.length === 0) return;
    const selectedId = locationScopePath.at(-1)!;
    const nextPath = locationScopePath.slice(0, -1);
    setSelectedLocationId(selectedId);
    setLocationScopePath(nextPath);
    setLocationQuery("");
    navigateHubRoute("world", "overview", false, {
      worldSection: "locations", locationScopePath: nextPath, locationId: selectedId,
    });
    setAnnouncement(`${nextPath.length ? locationById.get(nextPath.at(-1)!)?.name : envelope.world.name} opened`);
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

  function selectPartySection(characterId: string, requestedSection: PartySectionId, replace = false) {
    const nextSection = normalizePartySection(requestedSection) as PartySectionId;
    const nextCharacterId = envelope.party.some((member) => member.id === characterId)
      ? characterId
      : envelope.party[0]?.id ?? null;
    setPartyCharacterId(nextCharacterId);
    setPartySection(nextSection);
    navigateHubRoute("party", "overview", replace, {
      partySection: nextSection,
      characterId: nextCharacterId,
    });
    setAnnouncement(nextCharacterId
      ? `${envelope.party.find((member) => member.id === nextCharacterId)?.name ?? "Character"} ${nextSection} opened`
      : "Party roster opened");
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
      campaignDetailsAbort.current?.abort();
      setEnvelope((current) => ({
        ...current,
        campaign: { ...current.campaign, premise: result.premise },
        objectQueries: {},
      }));
      setCampaignDetails((current) => current.data
        ? { ...current, data: { ...current.data, premise: result.premise } }
        : current);
      dispatchObjectUi({ type: "write-confirmed", objectId: CAMPAIGN_SUMMARY_OBJECT_ID });
      setAnnouncement(result.replayed
        ? "Campaign premise confirmed after retry"
        : result.noOp ? "Campaign premise already matched the saved campaign" : "Campaign premise saved");
      const refreshed = await requestHub(perspective, contextSelection.selectedCampaignId, false, true);
      if (refreshed) {
        // The authoritative refresh covers a Campaign notice delivered while this write was
        // completing. A later independent notice remains queued by the event listener.
        setPendingChange((current) => current === CAMPAIGN_SUMMARY_OBJECT_ID ? null : current);
      }
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

  const visibleCampaign = campaignDetails.data ? {
    ...campaignDetails.data,
    title: envelope.campaign.title,
    subtitle: envelope.campaign.subtitle,
    status: envelope.campaign.status,
    premise: envelope.campaign.premise,
    objective: envelope.campaign.objective,
  } : envelope.campaign;

  function renderActiveView() {
    if (activeTab === "rules" && (itemRoute.kind === "registry-item" || itemRoute.kind === "registry-recipe")) {
      const compatible = itemRoute.campaignId === contextSelection.selectedCampaignId
        && itemRoute.perspective === perspective;
      return compatible && loadItemRegistryPage && loadItemDefinition ? <ItemRegistryWorkspace
        key={`${envelope.applicationId}:${contextSelection.selectedCampaignId}:${perspective}:rules-content`}
        route={itemRoute}
        campaignId={contextSelection.selectedCampaignId}
        perspective={perspective}
        loadPage={loadItemRegistryPage}
        loadDefinition={loadItemDefinition}
        loadRecipePage={loadRecipeRegistryPage}
        loadRecipeDefinition={loadRecipeDefinition}
      /> : <section className="view-unavailable" role="alert"><h1 id="main-view-heading">Related content unavailable</h1>
        <p>{!compatible ? "This link belongs to a different campaign or perspective."
          : "The content registry is not connected to this build."}</p></section>;
    }
    switch (activeTab) {
      case "campaign":
        return (
          <CampaignView
            campaign={visibleCampaign}
            detailsError={campaignDetails.error}
            detailsStatus={campaignDetails.status}
            hasValidatedDetails={campaignDetails.data !== null}
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
            onRetryDetails={loadCampaignDetails ? () => void requestCampaignDetails() : undefined}
            onSectionChange={selectCampaignSection}
            section={campaignSection}
            worldName={envelope.world.name}
          />
        );
      case "party":
        if (itemRoute.kind === "registry-item" || itemRoute.kind === "registry-recipe"
            || itemRoute.kind === "none" && partySection === "registry") {
          const compatibleRegistryRoute = itemRoute.kind !== "registry-item" && itemRoute.kind !== "registry-recipe" ||
            itemRoute.campaignId === contextSelection.selectedCampaignId && itemRoute.perspective === perspective;
          return compatibleRegistryRoute && loadItemRegistryPage && loadItemDefinition ? <ItemRegistryWorkspace
            key={`${envelope.applicationId}:${contextSelection.selectedCampaignId}:${perspective}:registry`}
            route={itemRoute}
            campaignId={contextSelection.selectedCampaignId}
            perspective={perspective}
            loadPage={loadItemRegistryPage}
            loadDefinition={loadItemDefinition}
            loadRecipePage={loadRecipeRegistryPage}
            loadRecipeDefinition={loadRecipeDefinition}
          /> : <section className="view-unavailable" role="alert"><h1 id="main-view-heading">Registry unavailable</h1>
            <p>{!compatibleRegistryRoute ? "This link belongs to a different campaign or perspective."
              : "The item registry is not connected to this build."}</p></section>;
        }
        if (Boolean(loadDeferredSection && playerPreview) || itemRoute.kind === "none" || itemRoute.kind === "inventory" &&
            itemRoute.campaignId === contextSelection.selectedCampaignId &&
            itemRoute.perspective === perspective &&
            envelope.party.some((member) => member.id === itemRoute.characterId)) {
          const summaryOnly = Boolean(loadDeferredSection && playerPreview);
          const itemCharacterId = itemRoute.kind === "inventory" || itemRoute.kind === "item"
            ? itemRoute.characterId
            : null;
          const selectedId = summaryOnly && envelope.party.some((member) => member.id === itemCharacterId)
            ? itemCharacterId!
            : itemRoute.kind === "inventory" ? itemRoute.characterId : selectedPartyCharacterId ?? undefined;
          const selectedSection = summaryOnly ? "overview"
            : itemRoute.kind === "inventory" ? "inventory" : partySection;
          const inventoryReturn = !summaryOnly && itemRoute.kind === "inventory" && selectedId
            ? readInventoryReturn(window.history.state, selectedId)
            : null;
          return <CharacterWorkspace
            key={`${envelope.applicationId}:${envelope.stateSpaceId}:${contextSelection.selectedCampaignId}:${envelope.audience.seat}:${perspective}`}
            navigationCharacterId={selectedId}
            navigationSection={selectedSection}
            onNavigationChange={selectPartySection}
            inventoryReturn={inventoryReturn}
            loadCharacterSheet={!summaryOnly && loadCharacterSheet ? readCharacterSheet : undefined}
            loadCharacterDetails={!summaryOnly && loadCharacterDetails ? readCharacterDetails : undefined}
            loadCharacterInventory={!summaryOnly && loadCharacterInventory ? readCharacterInventory : undefined}
            loadInventoryContainer={!summaryOnly && loadInventoryContainer ? readInventoryContainer : undefined}
            loading={hubBusy}
            onRetry={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}
            onOpenItem={(characterId, itemId, context) => {
              const inventory = { kind: "inventory" as const, characterId,
                campaignId: contextSelection.selectedCampaignId, perspective };
              navigateItemRoute(inventory, true, context);
              navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
            }}
            party={envelope.party}
            summaryOnly={summaryOnly}
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
          loadInventoryContainer={loadInventoryContainer ? readInventoryContainer : undefined}
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
        return <RulesView campaignId={contextSelection.selectedCampaignId} perspective={perspective}
          loadRules={loadRules} rules={envelope.rules} />;
      case "content":
        return <InstalledContentView loadContent={loadContent}
          resolutionFingerprint={envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? null} />;
      case "world":
      default:
        return (
          <WorldView
            directoriesDeferred={Boolean(loadDeferredSection)}
            deferredNotice={deferredNotice}
            campaign={envelope.campaign}
            currentLocation={currentLocation}
            filteredLocations={visibleLocations}
            locationScope={activeLocationScope}
            locationScopeBusy={locationScopeBusy}
            locationScopeError={locationScopeError}
            mapScopeError={activeMapScopeFailed ? locationScopeError : ""}
            mapScopeState={mapScopeState}
            locationSection={locationSection}
            perspective={perspective}
            selectedFactionId={selectedFactionId}
            selectedPersonId={selectedPersonId}
            onLocationSelect={openLocationScope}
            onLocationScopeBack={openParentLocationScope}
            onLoadMoreLocations={() => activeLocationScope?.nextCursor
              ? void requestWorldScope(activeLocationScope.id, activeLocationScope.nextCursor)
              : undefined}
            onRetryLocationScope={() => void requestWorldScope(activeLocationScopeId, null, true)}
            onRetryMapScope={() => activeMapScopeId
              ? void requestWorldScope(activeMapScopeId, null, true)
              : undefined}
            onQueryChange={(query) => setLocationQuery(query.slice(0, 80))}
            onLocationSectionChange={(section) => {
              const nextSection = normalizeLocationSection(section, perspective) as LocationSectionId;
              setLocationSection(nextSection);
              setAnnouncement(`${selectedLocation?.name ?? "Location"} ${nextSection} opened`);
            }}
            onFactionSelect={(factionId) => {
              dispatchObjectUi({ type: "faction-selected", factionId });
              setAnnouncement(
                `${envelope.world.factions.find((faction) => faction.id === factionId)?.name ?? "Faction"} selected`,
              );
            }}
            factionDirectoryBusy={hubBusy && worldSection === "factions"}
            onLoadMoreFactions={() => void requestFactionPage(envelope.world.factionDirectory?.nextCursor ?? null)}
            activeMapId={effectiveActiveMapId}
            onMapChange={(mapId) => {
              const nextMapId = normalizeMapId(
                envelope.world.maps,
                mapId,
                envelope.world.rootMapId,
              ) as string;
              if (nextMapId === effectiveActiveMapId) return;
              setActiveMapId(nextMapId);
              setSelectedMapFeatureId("");
              const map = resolveMapDocument(envelope.world.maps, nextMapId);
              if (map) void requestWorldScope(map.subject.id);
              setAnnouncement(
                `${resolveMapDocument(envelope.world.maps, nextMapId)?.subject.name ?? "Map"} map opened`,
              );
            }}
            onMapFeatureSelect={(featureId) => {
              const map = resolveMapDocument(envelope.world.maps, effectiveActiveMapId);
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
          chapter={campaignDetails.data?.chapter ?? (loadCampaignDetails
            ? campaignDetails.status === "error" ? "Campaign details unavailable" : "Campaign details loading"
            : envelope.campaign.chapter)}
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
