import type { ReadyHubEnvelope } from "./hub-types";
import { parseCursorCheckpoint, parseObjectChange } from "./object-change.js";
import type { ResourceInvalidationReason } from "./resource-store";

/** Browser dependencies only. Unknown objects retain compatibility recovery, never silence. */
export function objectConsumers(id: string) {
  const item = [
    "dnd2024.object.inventory-item-instance-records", "dnd2024.object.inventory-item-definition-records",
    "dnd2024.object.inventory-item-recipe-record", "dnd2024.object.inventory-item-activity-record",
  ].includes(id);
  const character = item || id === "dnd2024.object.character-dossier-records" ||
    id === "dnd2024.object.campaign-summary";
  const world = item || character || id === "dnd2024.object.faction-directory-page";
  const known = item || character || [
    "dnd2024.object.faction-directory-page",
    "dnd2024.object.campaign-location-visits",
    "dnd2024.object.world-campaign-directory",
  ].includes(id);
  return { item, character, world, known };
}

type Options = {
  createSource?: (url: string) => EventSource;
  invalidate: (reason: ResourceInvalidationReason) => void;
  changed: (notice: NonNullable<ReturnType<typeof parseObjectChange>>) => void;
  /** A successfully reopened SSE stream warrants one bounded active-view revalidation. */
  reconnected?: () => void;
  lifecycle?: Window;
};

/** One authorized scope owns one stream and cursor. Teardown fences even queued old frames. */
export function subscribeScopedChanges(envelope: ReadyHubEnvelope, {
  createSource = (url) => new EventSource(url), invalidate, changed, reconnected, lifecycle = window,
}: Options) {
  const parameters = new URLSearchParams({ page: "dnd2024-play", application: envelope.applicationId,
    stateSpace: envelope.stateSpaceId, perspective: envelope.audience.perspective, cursor: "0" });
  let source: EventSource | null = null;
  let disposed = false;
  let hidden = false;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let retryKind: "closed" | "connecting" | undefined;
  let terminalAttempts = 0;
  // CLOSED failures (including temporary non-200 responses) get prompt fresh
  // attempts. A CONNECTING instance keeps the browser's native retry until a
  // slower watchdog replaces it. The shared eight-attempt budget remains
  // finite while covering a multi-minute restart in either state.
  const closedRetryDelays = [1_000, 2_000, 4_000, 8_000, 15_000, 30_000, 60_000, 120_000];
  const connectingWatchdogDelays = [20_000, 20_000, 30_000, 60_000, 120_000, 120_000, 120_000, 120_000];
  // EventSource may report a transport failure before its first application
  // frame, and pagehide/pageshow creates a new instance. Recovery therefore
  // belongs to the scoped subscription, not one source instance.
  let recovering = false;
  const cancelRetry = () => { clearTimeout(retryTimer); retryTimer = undefined; retryKind = undefined; };
  function retryUnconfirmedSource(expected: EventSource) {
    if (disposed || hidden || retryTimer !== undefined || terminalAttempts >= closedRetryDelays.length) return;
    const kind = expected.readyState === 2 ? "closed" : "connecting";
    const delays = kind === "closed" ? closedRetryDelays : connectingWatchdogDelays;
    retryKind = kind;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      retryKind = undefined;
      if (disposed || hidden || source !== expected || !recovering) return;
      terminalAttempts += 1;
      expected.close();
      source = null;
      connect();
    }, delays[terminalAttempts]);
  }
  function connect() {
    if (disposed || hidden || source) return;
    const current = createSource(`/api/changes?${parameters}`);
    source = current;
    let cursor = 0;
    const active = () => !disposed && source === current;
    current.addEventListener("invalidate", (event) => {
      if (!active()) return;
      try {
        if (JSON.parse(event.data).reason === "connected") {
          const resumed = recovering;
          recovering = false;
          terminalAttempts = 0;
          cancelRetry();
          if (resumed) reconnected?.();
          return;
        }
      } catch { /* Unreadable recovery evidence invalidates the current scope. */ }
      invalidate("stream-recovery");
    });
    current.addEventListener("object-change", (event) => {
      if (!active()) return;
      const notice = parseObjectChange(event.data, envelope, cursor);
      if (!notice) return;
      cursor = notice.cursor;
      changed(notice);
    });
    current.addEventListener("cursor", (event) => {
      if (active()) cursor = parseCursorCheckpoint(event.data, cursor);
    });
    current.addEventListener("error", () => {
      if (!active()) return;
      if (!recovering) {
        recovering = true;
        invalidate("stream-error");
      }
      // Native retry remains the fast path, but some browsers can keep a dead
      // source in CONNECTING after a sustained outage. Replace either an
      // unconfirmed CONNECTING or CLOSED source on the bounded watchdog.
      if (current.readyState === 2 && retryKind === "connecting") cancelRetry();
      retryUnconfirmedSource(current);
    });
  }
  const hide = () => { hidden = true; recovering = true; cancelRetry(); source?.close(); source = null; invalidate("pagehide"); };
  const show = () => {
    if (!hidden) return;
    hidden = false;
    terminalAttempts = 0;
    connect();
  };
  lifecycle.addEventListener("pagehide", hide);
  lifecycle.addEventListener("pageshow", show);
  connect();
  return () => {
    disposed = true;
    cancelRetry();
    source?.close(); source = null;
    lifecycle.removeEventListener("pagehide", hide);
    lifecycle.removeEventListener("pageshow", show);
  };
}
