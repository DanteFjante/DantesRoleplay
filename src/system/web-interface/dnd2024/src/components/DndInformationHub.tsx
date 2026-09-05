"use client";

import { lazy, Suspense, useCallback, useEffect, useRef, useState } from "react";

import { resolveCampaignWorldTarget } from "../data/campaign-navigation";
import { ITEM_ROUTE_EVENT, navigateItemRoute, parseItemRoute } from "../data/item-view-route";
import { preserveLastGoodPartyData } from "../data/section-state";
import { ViewReadError } from "../data/view-read-client";
import type {
  CampaignSectionId,
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
const ItemWorkspace = lazy(() => import("./items/ItemWorkspace")
  .then((module) => ({ default: module.ItemWorkspace })));
const PlayConversationPanel = lazy(() => import("./PlayConversationPanel")
  .then((module) => ({ default: module.PlayConversationPanel })));
const CurrentViewPreview = lazy(() => import("./PreviewViews")
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

export function DndInformationHub({
  initialEnvelope,
  loadEnvelope,
  loadRules,
  loadContent,
  loadCharacter,
}: {
  initialEnvelope: ReadyHubEnvelope;
  loadEnvelope?: HubEnvelopeLoader;
  loadRules?: RulesLoader;
  loadContent: ContentLoader;
  loadCharacter?: (envelope: ReadyHubEnvelope, actorId: string, signal: AbortSignal) => Promise<import("../data/hub-types").PartyMemberReadModel>;
}) {
  const [envelope, setEnvelope] = useState(initialEnvelope);
  const readCharacter = useCallback((id: string, signal: AbortSignal) => {
    if (!loadCharacter) throw new Error("Character loading is unavailable.");
    return loadCharacter(envelope, id, signal);
  }, [envelope, loadCharacter]);
  const [itemRoute, setItemRoute] = useState(() => parseItemRoute(window.location.hash));
  const [activeTab, setActiveTab] = useState<MainTabId>(() => parseItemRoute(window.location.hash).kind === "none" ? "world" : "party");
  useEffect(() => {
    const changed = () => {
      const next = parseItemRoute(window.location.hash);
      setItemRoute(next);
      if (next.kind !== "none") setActiveTab("party");
      else if (window.history.state?.itemMainTab) setActiveTab(normalizeMainTab(window.history.state.itemMainTab) as MainTabId);
    };
    for (const event of ["popstate", "hashchange", ITEM_ROUTE_EVENT]) window.addEventListener(event, changed);
    return () => { for (const event of ["popstate", "hashchange", ITEM_ROUTE_EVENT]) window.removeEventListener(event, changed); };
  }, []);
  const [campaignSection, setCampaignSection] = useState<CampaignSectionId>("overview");
  const [worldSection, setWorldSection] = useState<WorldSectionId>("overview");
  const [locationSection, setLocationSection] = useState<LocationSectionId>("details");
  const [selectedLocationId, setSelectedLocationId] = useState(initialEnvelope.world.currentLocationId);
  const [selectedFactionId, setSelectedFactionId] = useState(initialEnvelope.world.factions[0]?.id ?? "");
  const [selectedPersonId, setSelectedPersonId] = useState(initialEnvelope.world.people[0]?.id ?? "");
  const [activeMapId, setActiveMapId] = useState(
    normalizeMapId(initialEnvelope.world.maps, initialEnvelope.world.rootMapId, initialEnvelope.world.rootMapId) as string,
  );
  const [selectedMapFeatureId, setSelectedMapFeatureId] = useState("");
  const [locationQuery, setLocationQuery] = useState("");
  const [announcement, setAnnouncement] = useState("World view ready");
  const [hubBusy, setHubBusy] = useState(false);
  const [hubError, setHubError] = useState("");
  const [serverChanged, setServerChanged] = useState(false);
  useEffect(() => {
    const invalidate = () => setServerChanged(true);
    window.addEventListener("dnd2024-view-invalidated", invalidate);
    return () => window.removeEventListener("dnd2024-view-invalidated", invalidate);
  }, []);
  const hubRequestSequence = useRef(0);

  const perspective = envelope.audience.perspective;
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
      setServerChanged(false);
      setLocationSection(
        normalizeLocationSection(
          locationSection,
          readyEnvelope.audience.perspective,
        ) as LocationSectionId,
      );
      if (campaignChanged || !readyEnvelope.world.locations.some((location) => location.id === selectedLocationId)) {
        setSelectedLocationId(readyEnvelope.world.currentLocationId);
      }
      if (campaignChanged || !readyEnvelope.world.factions.some((faction) => faction.id === selectedFactionId)) {
        setSelectedFactionId(readyEnvelope.world.factions[0]?.id ?? "");
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
      setHubError("The view could not be changed. Your current information is still available.");
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

  useEffect(() => {
    markActiveViewReady(activeTab);
  }, [activeTab]);

  function focusViewHeading() {
    window.requestAnimationFrame(() => document.querySelector<HTMLElement>("#main-view-heading")?.focus());
  }

  function selectTab(tab: MainTabId) {
    const nextTab = normalizeMainTab(tab) as MainTabId;
    if (nextTab === activeTab) return;
    if (itemRoute.kind !== "none") navigateItemRoute(null, false, null, nextTab);
    setActiveTab(nextTab);
    setAnnouncement(`${nextTab === "current" ? "Current view" : nextTab} opened`);
    focusViewHeading();
  }

  function selectWorldSection(section: WorldSectionId) {
    const nextSection = normalizeWorldSection(section) as WorldSectionId;
    setWorldSection(nextSection);
    setAnnouncement(`World ${nextSection} opened`);
    focusViewHeading();
  }

  function selectCampaignSection(section: CampaignSectionId) {
    const nextSection = normalizeCampaignSection(section) as CampaignSectionId;
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
    setActiveTab("world");
    setAnnouncement(`${location.name} opened from Campaign`);
    focusViewHeading();
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
    setSelectedFactionId(factionId);
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
            onOpenFaction={openCampaignFaction}
            onOpenLocation={openCampaignLocation}
            onOpenPerson={openCampaignPerson}
            onSectionChange={selectCampaignSection}
            section={campaignSection}
            worldName={envelope.world.name}
          />
        );
      case "party":
        return <ItemWorkspace
          key={`${envelope.applicationId}:${envelope.stateSpaceId}:${contextSelection.selectedCampaignId}:${envelope.audience.seat}:${perspective}`}
          route={itemRoute}
          campaignId={contextSelection.selectedCampaignId}
          perspective={perspective}
          loadCharacter={loadCharacter ? readCharacter : undefined}
          loading={hubBusy}
          onRetry={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}
          party={envelope.party}
        />;
      case "current":
        return (
          <div className="current-play-workspace">
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
              setSelectedFactionId(factionId);
              setAnnouncement(
                `${envelope.world.factions.find((faction) => faction.id === factionId)?.name ?? "Faction"} selected`,
              );
            }}
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
      />
      {hubError ? <p className="perspective-notice" role="alert">{hubError}</p> : null}
      {serverChanged ? <div className="perspective-notice" role="status">
        The server changed or the live connection was interrupted. Showing the last loaded view.
        <button type="button" disabled={hubBusy} onClick={() => void requestHub(perspective, contextSelection.selectedCampaignId, false, true)}>Refresh view</button>
      </div> : null}
      <div className="information-hub__body">
        <MainNavigation
          activeTab={activeTab}
          chapter={envelope.campaign.chapter}
          progress={envelope.campaign.progress}
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
