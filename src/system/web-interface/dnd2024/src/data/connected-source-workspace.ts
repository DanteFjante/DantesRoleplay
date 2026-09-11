import type { ConnectedCampaignEnvelope, DeferredHubSection, ReadyHubEnvelope } from "./hub-types";
import { ViewReadError } from "./view-read-client.ts";

type ReadFamily = DeferredHubSection | "campaign-details" | "factions";
type QueuedRead = {
  ticket: SourceReadTicket;
  signal?: AbortSignal;
  resolve: (lease: SourceReadLease) => void;
  reject: (reason: unknown) => void;
  abort?: () => void;
};
type Workspace = {
  generation: number;
  reads: Map<ReadFamily, number>;
  activeMerge: QueuedRead | null;
  queuedMerges: QueuedRead[];
};
/**
 * A narrow adapter over the existing hub Redux store. Keeping the adapter
 * structural lets this bootstrap-safe module avoid importing Redux before the
 * lazy hub store has been created.
 */
export type ConnectedSourceStateOwner = {
  get: (scope: string) => ConnectedCampaignEnvelope | null;
  replace: (scope: string, value: ConnectedCampaignEnvelope, bytes: number) => void;
  update: (scope: string, value: ConnectedCampaignEnvelope, bytes: number) => void;
  clear: () => void;
  metrics: () => { retainedBytes: number };
};
export type SourceReadTicket = Readonly<{ scope: string; generation: number; family: ReadFamily; token: number }>;
export type SourceReadLease = Readonly<{
  ticket: SourceReadTicket;
  source: ConnectedCampaignEnvelope;
  release: () => void;
}>;
const replaced = () => new DOMException("View replaced", "AbortError");
const MAXIMUM_QUEUED_MERGES = 8;
export const MAXIMUM_CONNECTED_SOURCE_BYTES = 8 * 1024 * 1024;

function sourceBytes(value: ConnectedCampaignEnvelope) {
  try { return new TextEncoder().encode(JSON.stringify(value)).byteLength; }
  catch { return MAXIMUM_CONNECTED_SOURCE_BYTES + 1; }
}

function deferredProjectionInput(value: ConnectedCampaignEnvelope) {
  // Rules are projected independently and no deferred source reader consumes
  // them. Do not retain that completed bootstrap collection merely because a
  // source lease needs the remaining authorized projection inputs.
  return value.rules?.length ? { ...value, rules: [] } : value;
}

export const connectedSourceScope = (source: ConnectedCampaignEnvelope) => JSON.stringify([
  source.applicationId, source.stateSpaceId, source.contextSelection.selectedWorldId, source.campaign.id,
  source.audience.seat, source.audience.perspective ?? source.audience.seat,
  (source.party ?? [{ ...source.actor, current: true }]).filter((member) => member.current).map((member) => member.id).sort(),
  source.campaign.projection?.resolutionFingerprint ?? "no-resolution",
]);
export const projectedSourceScope = (envelope: ReadyHubEnvelope) => JSON.stringify([
  envelope.applicationId, envelope.stateSpaceId,
  envelope.contextSelection?.selectedWorldId ?? envelope.world.id,
  envelope.contextSelection?.selectedCampaignId ?? envelope.revision,
  envelope.audience.seat, envelope.audience.perspective,
  envelope.party.filter((member) => member.isCurrent).map((member) => member.id).sort(),
  envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? "no-resolution",
]);

/** Coordinates bounded Redux-backed inputs for deferred projection. Every
 * bootstrap and overlapping read has an explicit identity, even when values
 * are equal; completed view projections remain with their dedicated owners. */
export class ConnectedSourceWorkspace {
  readonly #entries = new Map<string, Workspace>();
  #sourceState: ConnectedSourceStateOwner | null = null;
  #nextGeneration = 0;
  #nextRead = 0;

  /** The caller supplies the already-created hub store; this class never creates a backing store. */
  attach(sourceState: ConnectedSourceStateOwner) {
    if (this.#sourceState && this.#sourceState !== sourceState)
      throw new Error("The connected source workspace is already attached to another hub store.");
    this.#sourceState = sourceState;
  }

  replace(scope: string, value: ConnectedCampaignEnvelope) {
    const input = deferredProjectionInput(value);
    const bytes = sourceBytes(input);
    if (bytes > MAXIMUM_CONNECTED_SOURCE_BYTES)
      throw new ViewReadError("incompatible-data", "The connected source exceeded its staging bound.");
    // The Redux source input is scoped confirmed state, while this workspace
    // only leases it to active readers. A new authority scope retires every
    // previous raw input and its flight metadata immediately.
    const sourceState = this.#requireStateOwner();
    for (const entry of this.#entries.values()) this.#retire(entry);
    this.#entries.clear();
    sourceState.replace(scope, input, bytes);
    this.#entries.set(scope, {
      generation: ++this.#nextGeneration, reads: new Map(), activeMerge: null, queuedMerges: [],
    });
  }

  get(scope: string) { return this.#source(scope) ?? undefined; }

  begin(scope: string, family: ReadFamily): SourceReadTicket {
    const entry = this.#entries.get(scope);
    if (!entry) throw replaced();
    return this.#ticket(scope, entry, family);
  }

  /** Serializes source producers whose returned fields overlap. The ticket is issued at
   * request start, but the source snapshot is selected only when this lease becomes active. */
  acquireMerge(scope: string, family: ReadFamily, signal?: AbortSignal): Promise<SourceReadLease> {
    const entry = this.#entries.get(scope);
    if (!entry || signal?.aborted) return Promise.reject(replaced());
    const ticket = this.#ticket(scope, entry, family);
    return new Promise((resolve, reject) => {
      const queued: QueuedRead = { ticket, signal, resolve, reject };
      const superseded = entry.queuedMerges.findIndex((candidate) => candidate.ticket.family === family);
      if (superseded >= 0) this.#rejectQueued(entry.queuedMerges.splice(superseded, 1)[0]);
      if (entry.queuedMerges.length >= MAXIMUM_QUEUED_MERGES) {
        reject(replaced());
        return;
      }
      if (signal) {
        queued.abort = () => {
          // Cancellation retires the ticket itself, not only callers that
          // remember to pass this signal again at the commit boundary.
          if (entry.reads.get(family) === ticket.token) entry.reads.delete(family);
          const index = entry.queuedMerges.indexOf(queued);
          if (index >= 0) {
            entry.queuedMerges.splice(index, 1);
          } else if (entry.activeMerge === queued) {
            entry.activeMerge = null;
            this.#grantNext(scope, entry);
          }
          reject(replaced());
        };
        signal.addEventListener("abort", queued.abort, { once: true });
        if (signal.aborted) queued.abort();
      }
      if (!signal?.aborted) entry.queuedMerges.push(queued);
      this.#grantNext(scope, entry);
    });
  }

  current(ticket: SourceReadTicket, signal?: AbortSignal) {
    const entry = this.#entries.get(ticket.scope);
    if (signal?.aborted || !entry || entry.generation !== ticket.generation ||
        entry.reads.get(ticket.family) !== ticket.token) throw replaced();
    const value = this.#source(ticket.scope);
    if (!value) throw replaced();
    return value;
  }

  update(ticket: SourceReadTicket, value: ConnectedCampaignEnvelope, signal?: AbortSignal) {
    this.current(ticket, signal);
    const bytes = sourceBytes(value);
    if (bytes > MAXIMUM_CONNECTED_SOURCE_BYTES)
      throw new ViewReadError("incompatible-data", "The connected source exceeded its staging bound.");
    this.#requireStateOwner().update(ticket.scope, value, bytes);
  }

  invalidate() {
    for (const entry of this.#entries.values()) {
      this.#retire(entry);
      entry.generation = ++this.#nextGeneration;
      entry.reads.clear();
    }
  }

  metrics() {
    const retainedBytes = this.#sourceState?.metrics().retainedBytes ?? 0;
    return { retainedScopes: retainedBytes > 0 ? 1 : 0, retainedBytes };
  }

  /** Authority/session teardown revokes raw projection inputs as well as leases. */
  clear() {
    for (const entry of this.#entries.values()) this.#retire(entry);
    this.#entries.clear();
    this.#requireStateOwner().clear();
  }

  #ticket(scope: string, entry: Workspace, family: ReadFamily) {
    const token = ++this.#nextRead;
    entry.reads.set(family, token);
    return Object.freeze({ scope, generation: entry.generation, family, token });
  }

  #grantNext(scope: string, entry: Workspace) {
    if (entry.activeMerge) return;
    while (entry.queuedMerges.length > 0) {
      const queued = entry.queuedMerges.shift()!;
      try {
        const source = this.current(queued.ticket, queued.signal);
        entry.activeMerge = queued;
        let released = false;
        queued.resolve(Object.freeze({
          ticket: queued.ticket,
          source,
          release: () => {
            if (released) return;
            released = true;
            if (queued.abort) queued.signal?.removeEventListener("abort", queued.abort);
            const current = this.#entries.get(scope);
            if (current !== entry || current.activeMerge !== queued) return;
            current.activeMerge = null;
            this.#grantNext(scope, current);
          },
        }));
        return;
      } catch (error) {
        if (queued.abort) queued.signal?.removeEventListener("abort", queued.abort);
        queued.reject(error);
      }
    }
  }

  #rejectQueued(queued: QueuedRead) {
    if (queued.abort) queued.signal?.removeEventListener("abort", queued.abort);
    queued.reject(replaced());
  }

  #retire(entry: Workspace) {
    for (const queued of entry.queuedMerges.splice(0)) this.#rejectQueued(queued);
    if (entry.activeMerge?.abort)
      entry.activeMerge.signal?.removeEventListener("abort", entry.activeMerge.abort);
    entry.activeMerge = null;
  }

  #source(scope: string) {
    return this.#sourceState?.get(scope) ?? null;
  }

  #requireStateOwner() {
    if (!this.#sourceState) throw new Error("The connected source workspace is not attached to a hub store.");
    return this.#sourceState;
  }
}
