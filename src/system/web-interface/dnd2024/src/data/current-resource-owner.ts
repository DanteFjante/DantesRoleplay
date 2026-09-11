import type { CurrentViewRequest, CurrentViewUpdate } from "./object-resources";
import { RESOURCE_FRESHNESS_MS } from "./resource-policy";
import { RequestCoordinator, type CoordinatedRequest } from "./request-coordinator";
import {
  allocateCurrentRequestToken,
  commitCurrent,
  commitCurrentBootstrap,
  currentActions,
  currentScope,
  peekCurrentDisplay,
  type CurrentDisplay,
  type HubStore,
} from "./hub-store";
import type { CurrentSituationReadModel, ReadyHubEnvelope, WorldLocation } from "./hub-types";
import { ViewReadError } from "./view-read-client";
import type { ResourceInvalidationReason } from "./resource-store";
import { CurrentViewAuthorizationError } from "./current-view-error.js";

const maximumBytes = 2 * 1024 * 1024;
const bytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;
type LogicalFlight = { scope: string; generation: number; requestToken: number; consumers: number; committed: boolean; denied: boolean };
const currentResumeQueryId = "dnd2024.query.campaign-resume";
const currentSceneQueryId = "dnd2024.query.current-scene";
const fingerprint = (value: unknown) => typeof value === "string" && /^[0-9a-f]{64}$/iu.test(value);

function isCurrentReadEvidence(value: unknown, queryId: string) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const evidence = value as Record<string, unknown>;
  return evidence.qualifiedQueryId === queryId &&
    fingerprint(evidence.stateSpaceFingerprint) &&
    (fingerprint(evidence.resolutionFingerprint) || evidence.resolutionFingerprint === "none") &&
    fingerprint(evidence.outputSchemaHash) && fingerprint(evidence.resultFingerprint) &&
    fingerprint(evidence.sourceRevisionFingerprint);
}

function bootstrapCurrentProjection(envelope: ReadyHubEnvelope) {
  const situation = envelope.currentSituation;
  const projection = envelope.objectQueries?.currentPlay;
  // Bootstrap envelopes can intentionally carry an unloaded Current placeholder.
  // It is staging state, not a completed Current read, and must never become a
  // fresh Redux result that prevents the registered Current query from running.
  if (!situation || !projection || !isCurrentReadEvidence(projection.resume, currentResumeQueryId)) return null;
  if (situation.status !== "ready") return projection;
  if (!projection.scene || !isCurrentReadEvidence(projection.scene, currentSceneQueryId)) return null;
  if (projection.resume.stateSpaceFingerprint !== projection.scene.stateSpaceFingerprint ||
      projection.resume.resolutionFingerprint !== projection.scene.resolutionFingerprint) return null;
  return projection;
}

export type CurrentViewResourceOwnerOptions = {
  store: HubStore;
  readCurrent: (request: CurrentViewRequest, signal: AbortSignal) => Promise<CurrentViewUpdate>;
  maximumAgeMs?: number;
};

function isCurrentDisplay(value: unknown): value is CurrentDisplay {
  if (!value || typeof value !== "object") return false;
  const display = value as Record<string, unknown>;
  return Boolean(display.situation && typeof display.situation === "object") &&
    (display.location === null || Boolean(display.location && typeof display.location === "object"));
}

export function currentDisplayFromUpdate(update: CurrentViewUpdate): CurrentDisplay {
  const situation = update.currentSituation as CurrentSituationReadModel;
  // An unplaced/unavailable scene must not inherit World's last current
  // location. Only an explicit ready-scene binding can select location detail.
  const selectedId = situation.status === "ready" && typeof situation.locationId === "string"
    ? situation.locationId : null;
  const locations = Array.isArray(update.world.locations) ? update.world.locations : [];
  // The projection has already field-normalized these rows. Take only the selected
  // location: Current never becomes a second World directory owner.
  const location = locations.find((candidate) => candidate && typeof candidate === "object" &&
    selectedId !== null && (candidate as { id?: unknown }).id === selectedId) as WorldLocation | undefined;
  return { situation, location: location ?? null,
    ...(update.projection === undefined ? {} : { projection: update.projection }) };
}

/**
 * Current has no completed-value cache of its own. Redux is the single bounded
 * owner; this class only coalesces active transports and fences obsolete scopes.
 */
export class CurrentViewResourceOwner {
  readonly #store: HubStore;
  readonly #coordinator = new RequestCoordinator();
  readonly #maximumAgeMs: number;
  readonly #current: CoordinatedRequest<CurrentViewRequest, CurrentDisplay>;
  #scope: string | null = null;
  #flight: LogicalFlight | null = null;

  constructor(options: CurrentViewResourceOwnerOptions) {
    this.#store = options.store;
    this.#maximumAgeMs = options.maximumAgeMs ?? RESOURCE_FRESHNESS_MS.currentView;
    this.#current = {
      key: (_, generation) => `current\u0000${generation}`,
      read: async (request, signal) => {
        let raw: CurrentViewUpdate;
        try { raw = await options.readCurrent(request, signal); }
        catch (error) {
          if (error instanceof CurrentViewAuthorizationError)
            throw new ViewReadError("authorization", error.message, { cause: error });
          throw error;
        }
        const { normalizeCurrentViewUpdate } = await import("./current-deferred-projection.js");
        const update = await normalizeCurrentViewUpdate(raw);
        if (!update) throw new ViewReadError("incompatible-data", "The Current View response could not be projected safely.");
        return currentDisplayFromUpdate(update);
      },
      validate: isCurrentDisplay,
      maximumBytes,
    };
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    _reason: ResourceInvalidationReason = "scope-replaced") {
    const scope = currentScope(envelope);
    if (force || this.#scope !== scope) {
      this.#coordinator.replaceScope(scope, true);
      this.#store.dispatch(currentActions.scopeReplaced({ scope, force }));
      this.#flight = null;
    }
    this.#scope = scope;
    return scope;
  }

  /** Seed only the selected bootstrap scene; the original envelope remains staging data. */
  async seedBootstrap(envelope: ReadyHubEnvelope) {
    const scope = this.replaceScope(envelope);
    const generation = this.#store.getState().current.generation;
    const requestToken = allocateCurrentRequestToken();
    const projection = bootstrapCurrentProjection(envelope);
    if (!projection) return null;
    const { normalizeCurrentViewUpdate } = await import("./current-deferred-projection.js");
    const update = await normalizeCurrentViewUpdate({
      section: "current",
      currentSituation: envelope.currentSituation!,
      projection,
      world: { currentLocationId: envelope.world.currentLocationId, locations: envelope.world.locations },
    });
    if (!update) return null;
    const value = currentDisplayFromUpdate(update);
    if (bytes(value) > maximumBytes) return null;
    const active = this.#store.getState().current;
    if (active.scope !== scope || active.generation !== generation) return null;
    this.#store.dispatch(commitCurrentBootstrap({ scope, value }, {
      generation, requestToken, bytes: bytes(value), confirmedAt: Date.now(),
    }));
    return value;
  }

  async loadCurrent(request: CurrentViewRequest, signal?: AbortSignal, preferCached = true) {
    if (signal?.aborted) throw new ViewReadError("cancelled", "The Current request is no longer current.");
    const scope = this.replaceScope(request.envelope);
    if (signal?.aborted) throw new ViewReadError("cancelled", "The Current request is no longer current.");
    const cached = preferCached ? peekCurrentDisplay(this.#store.getState(), scope, this.#maximumAgeMs) : null;
    if (cached) {
      if (signal?.aborted) throw new ViewReadError("cancelled", "The Current request is no longer current.");
      return cached;
    }
    const generation = this.#store.getState().current.generation;
    const flight = this.#flight?.scope === scope && this.#flight.generation === generation
      ? this.#flight : this.#startFlight(scope, generation);
    flight.consumers += 1;
    try {
      const value = await this.#coordinator.load(this.#current, request, signal);
      if (signal?.aborted || this.#flight !== flight) throw new ViewReadError("cancelled", "The Current scope is no longer current.");
      if (!flight.committed) {
        this.#store.dispatch(commitCurrent({ scope, value }, {
          generation: flight.generation, requestToken: flight.requestToken,
          bytes: bytes(value), confirmedAt: Date.now(),
        }));
        flight.committed = true;
      }
      return value;
    } catch (error) {
      if (error instanceof ViewReadError && error.category === "authorization") this.#deny(flight);
      throw error;
    } finally {
      this.#release(flight);
    }
  }

  invalidateObject(_qualifiedId: string) { this.invalidateAll("object-change"); return true; }

  invalidateAll(_reason: ResourceInvalidationReason = "manual") {
    this.#coordinator.invalidate();
    this.#flight = null;
    this.#store.dispatch(currentActions.invalidated());
  }

  cacheMetrics() {
    const current = this.#store.getState().current;
    return { retainedEntries: current.entry ? 1 : 0, retainedBytes: current.retainedBytes };
  }

  #startFlight(scope: string, generation: number) {
    const flight = { scope, generation, requestToken: allocateCurrentRequestToken(), consumers: 0,
      committed: false, denied: false };
    this.#flight = flight;
    this.#store.dispatch(currentActions.currentRequestStarted({ scope, requestToken: flight.requestToken }));
    return flight;
  }

  #release(flight: LogicalFlight) {
    flight.consumers -= 1;
    if (flight.consumers > 0 || this.#flight !== flight) return;
    if (!flight.committed && !flight.denied)
      this.#store.dispatch(currentActions.currentRequestFinished({ scope: flight.scope, requestToken: flight.requestToken }));
    this.#flight = null;
  }

  #deny(flight: LogicalFlight) {
    if (flight.denied || this.#flight !== flight) return;
    const current = this.#store.getState().current;
    if (current.scope !== flight.scope || current.generation !== flight.generation) return;
    flight.denied = true;
    this.#store.dispatch(currentActions.currentDenied({ scope: flight.scope, requestToken: flight.requestToken }));
  }
}
