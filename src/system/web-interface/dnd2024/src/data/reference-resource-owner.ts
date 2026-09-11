import type { ReadyHubEnvelope, RulesReferencePublication } from "./hub-types";
import { RESOURCE_FRESHNESS_MS } from "./resource-policy";
import { RequestCoordinator, type CoordinatedRequest } from "./request-coordinator";
import { allocateReferenceRequestToken, characterScope, peekInstalledContentPage, peekRulesPublication,
  referenceActions, type HubStore } from "./hub-store";
import { installedContentRequestKey, type InstalledContentPage, type InstalledContentRequest } from "../server/effective-content";
import { ViewReadError } from "./view-read-client";

const rulesBytes = 4 * 1024 * 1024;
const pageBytes = 524_288;
const bytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;
type LogicalFlight = {
  scope: string;
  generation: number;
  requestToken: number;
  consumers: number;
  committed: boolean;
  denied: boolean;
};

function rulesPublication(value: unknown): value is RulesReferencePublication {
  return Boolean(value && typeof value === "object" && (value as { applicationId?: unknown }).applicationId === "dnd2024"
    && Array.isArray((value as { rules?: unknown }).rules));
}
function contentPage(value: unknown): value is InstalledContentPage {
  return Boolean(value && typeof value === "object" && Array.isArray((value as { records?: unknown }).records)
    && Array.isArray((value as { extensions?: unknown }).extensions));
}

export type ReferenceResourceOwnerOptions = {
  store: HubStore;
  readRules: (signal: AbortSignal) => Promise<RulesReferencePublication>;
  readContent: (request: InstalledContentRequest, signal: AbortSignal) => Promise<InstalledContentPage>;
  rulesMaximumAgeMs?: number;
  contentMaximumAgeMs?: number;
};

/**
 * Transport/in-flight coordination only. Redux holds every completed Rules and
 * Installed Content response, partitioned by the full authorized hub scope.
 */
export class ReferenceResourceOwner {
  readonly #store: HubStore;
  readonly #coordinator = new RequestCoordinator();
  readonly #rulesMaximumAgeMs: number;
  readonly #contentMaximumAgeMs: number;
  readonly #rules: CoordinatedRequest<undefined, RulesReferencePublication>;
  readonly #content: CoordinatedRequest<InstalledContentRequest, InstalledContentPage>;
  readonly #contentFlights = new Map<string, LogicalFlight>();
  #rulesFlight: LogicalFlight | null = null;
  #scope: string | null = null;

  constructor(options: ReferenceResourceOwnerOptions) {
    this.#store = options.store;
    this.#rulesMaximumAgeMs = options.rulesMaximumAgeMs ?? RESOURCE_FRESHNESS_MS.rulesReference;
    this.#contentMaximumAgeMs = options.contentMaximumAgeMs ?? RESOURCE_FRESHNESS_MS.installedContent;
    this.#rules = { key: (_, generation) => `rules\u0000${generation}`,
      read: (_, signal) => options.readRules(signal), validate: rulesPublication, maximumBytes: rulesBytes };
    this.#content = { key: (request, generation) => `content\u0000${this.#contentFlightKey(request)}\u0000${generation}`,
      read: options.readContent, validate: contentPage, maximumBytes: pageBytes };
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false) {
    const scope = characterScope(envelope);
    if (force || this.#scope !== scope) {
      this.#coordinator.replaceScope(scope, true);
      this.#store.dispatch(referenceActions.scopeReplaced({ scope, force }));
      this.#contentFlights.clear();
      this.#rulesFlight = null;
    }
    this.#scope = scope;
    return scope;
  }

  invalidate() {
    this.#coordinator.invalidate();
    this.#contentFlights.clear();
    this.#rulesFlight = null;
    this.#store.dispatch(referenceActions.invalidated());
  }

  async loadRules(envelope: ReadyHubEnvelope, signal?: AbortSignal, preferCached = true) {
    const scope = this.replaceScope(envelope);
    const cached = preferCached ? peekRulesPublication(this.#store.getState(), scope, this.#rulesMaximumAgeMs) : null;
    if (cached) return cached;
    const generation = this.#store.getState().references.generation;
    const flight = this.#rulesFlight?.scope === scope && this.#rulesFlight.generation === generation
      ? this.#rulesFlight
      : this.#startRulesFlight(scope, generation);
    flight.consumers += 1;
    try {
      const value = await this.#coordinator.load(this.#rules, undefined, signal);
      if (this.#rulesFlight !== flight) throw new ViewReadError("cancelled", "The rules scope is no longer current.");
      const expectedResolutionFingerprint = envelope.objectQueries?.campaignSummary?.resolutionFingerprint;
      if (expectedResolutionFingerprint && value.resolutionFingerprint !== expectedResolutionFingerprint)
        throw new ViewReadError("stale-data", "Published rules changed while this scope was loading.");
      if (envelope.audience.perspective === "player" && (value.audience !== "public"
        || value.rules.some((rule) => rule.visibility !== "public"))) {
        this.#denyRules(flight);
        throw new ViewReadError("authorization", "Published rules are not available for this audience.");
      }
      if (!flight.committed) {
        this.#store.dispatch({ type: referenceActions.rulesCommitted.type, payload: { scope, value },
          meta: { generation: flight.generation, requestToken: flight.requestToken, bytes: bytes(value), confirmedAt: Date.now() } });
        flight.committed = true;
      }
      return value;
    } catch (error) {
      if (error instanceof ViewReadError && error.category === "authorization")
        this.#denyRules(flight);
      throw error;
    } finally {
      this.#releaseRulesFlight(flight);
    }
  }

  async loadContent(envelope: ReadyHubEnvelope, request: InstalledContentRequest, signal?: AbortSignal,
    preferCached = true) {
    const scope = this.replaceScope(envelope);
    const key = installedContentRequestKey(request);
    const cached = preferCached
      ? peekInstalledContentPage(this.#store.getState(), scope, key, this.#contentMaximumAgeMs) : null;
    if (cached) return cached;
    const generation = this.#store.getState().references.generation;
    const flight = this.#contentFlights.get(key)?.scope === scope && this.#contentFlights.get(key)?.generation === generation
      ? this.#contentFlights.get(key)!
      : this.#startContentFlight(scope, generation, key);
    flight.consumers += 1;
    try {
      const value = await this.#coordinator.load(this.#content, request, signal);
      if (this.#contentFlights.get(key) !== flight)
        throw new ViewReadError("cancelled", "The installed-content scope is no longer current.");
      if (!flight.committed) {
        this.#store.dispatch({ type: referenceActions.contentCommitted.type, payload: { scope, key, value },
          meta: { generation: flight.generation, requestToken: flight.requestToken, bytes: bytes(value), confirmedAt: Date.now() } });
        flight.committed = true;
      }
      return value;
    } catch (error) {
      if (error instanceof ViewReadError && error.category === "authorization")
        this.#denyContent(key, flight);
      throw error;
    } finally {
      // The logical flight owns the coordinator key until every joined consumer
      // settles. Exact completed-query identity remains Redux's bounded key.
      this.#releaseContentFlight(key, flight);
    }
  }

  #contentFlightKey(request: InstalledContentRequest) {
    const exact = installedContentRequestKey(request);
    // A logical flight is registered before coordinator.load checks an
    // optional consumer signal. Its global request token is bounded, unlike a
    // legal 4KiB cursor, and cannot be mistaken for a new scope's flight.
    return `content-${this.#contentFlights.get(exact)?.requestToken ?? "retired"}`;
  }

  #startRulesFlight(scope: string, generation: number): LogicalFlight {
    const flight = { scope, generation, requestToken: allocateReferenceRequestToken(), consumers: 0,
      committed: false, denied: false };
    this.#rulesFlight = flight;
    this.#store.dispatch(referenceActions.rulesRequestStarted({ scope, requestToken: flight.requestToken }));
    return flight;
  }

  #releaseRulesFlight(flight: LogicalFlight) {
    flight.consumers -= 1;
    if (flight.consumers > 0 || this.#rulesFlight !== flight) return;
    if (!flight.committed && !flight.denied)
      this.#store.dispatch(referenceActions.requestFinished({ scope: flight.scope, key: "rules", requestToken: flight.requestToken }));
    this.#rulesFlight = null;
  }

  #denyRules(flight: LogicalFlight) {
    if (flight.denied) return;
    flight.denied = true;
    this.#store.dispatch(referenceActions.rulesDenied({ scope: flight.scope, requestToken: flight.requestToken }));
  }

  #startContentFlight(scope: string, generation: number, key: string): LogicalFlight {
    const flight = { scope, generation, requestToken: allocateReferenceRequestToken(), consumers: 0,
      committed: false, denied: false };
    this.#contentFlights.set(key, flight);
    this.#store.dispatch(referenceActions.contentRequestStarted({ scope, key, requestToken: flight.requestToken }));
    return flight;
  }

  #releaseContentFlight(key: string, flight: LogicalFlight) {
    flight.consumers -= 1;
    if (flight.consumers > 0 || this.#contentFlights.get(key) !== flight) return;
    if (!flight.committed && !flight.denied)
      this.#store.dispatch(referenceActions.requestFinished({ scope: flight.scope, key: `content:${key}`, requestToken: flight.requestToken }));
    this.#contentFlights.delete(key);
  }

  #denyContent(key: string, flight: LogicalFlight) {
    if (flight.denied) return;
    const current = this.#store.getState().references;
    if (this.#contentFlights.get(key) !== flight || current.scope !== flight.scope || current.generation !== flight.generation) return;
    // A denied page revokes the whole content collection for this scope. Fence
    // every different query already in flight before any of them can return a
    // now-private page to a caller.
    for (const candidate of this.#contentFlights.values()) candidate.denied = true;
    this.#contentFlights.clear();
    this.#store.dispatch(referenceActions.contentDenied({ scope: flight.scope, key, requestToken: flight.requestToken }));
  }
}
