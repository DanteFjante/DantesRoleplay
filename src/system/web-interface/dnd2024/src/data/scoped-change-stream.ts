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
  const known = item || character || [
    "dnd2024.object.faction-directory-page",
    "dnd2024.object.campaign-location-visits",
    "dnd2024.object.world-campaign-directory",
  ].includes(id);
  return { item, character, known };
}

type Options = {
  createSource?: (url: string) => EventSource;
  invalidate: (reason: ResourceInvalidationReason) => void;
  changed: (notice: NonNullable<ReturnType<typeof parseObjectChange>>) => void;
  lifecycle?: Window;
};

/** One authorized scope owns one stream and cursor. Teardown fences even queued old frames. */
export function subscribeScopedChanges(envelope: ReadyHubEnvelope, {
  createSource = (url) => new EventSource(url), invalidate, changed, lifecycle = window,
}: Options) {
  const parameters = new URLSearchParams({ page: "dnd2024-play", application: envelope.applicationId,
    stateSpace: envelope.stateSpaceId, perspective: envelope.audience.perspective, cursor: "0" });
  let source: EventSource | null = null;
  let disposed = false;
  function connect() {
    if (disposed || source) return;
    const current = createSource(`/api/changes?${parameters}`);
    source = current;
    let connected = false;
    let cursor = 0;
    const active = () => !disposed && source === current;
    current.addEventListener("invalidate", (event) => {
      if (!active()) return;
      try {
        const first = !connected && JSON.parse(event.data).reason === "connected";
        connected = true;
        if (first) return;
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
    current.addEventListener("error", () => { if (active()) invalidate("stream-error"); });
  }
  const hide = () => { source?.close(); source = null; invalidate("pagehide"); };
  lifecycle.addEventListener("pagehide", hide);
  lifecycle.addEventListener("pageshow", connect);
  connect();
  return () => {
    disposed = true;
    source?.close(); source = null;
    lifecycle.removeEventListener("pagehide", hide);
    lifecycle.removeEventListener("pageshow", connect);
  };
}
