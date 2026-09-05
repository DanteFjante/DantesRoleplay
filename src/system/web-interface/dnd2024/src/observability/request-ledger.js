export const DEVELOPMENT_OBSERVABILITY_KEY = "__DND2024_DEVELOPMENT_OBSERVABILITY__";

function requestMethod(input, init) {
  return String(init?.method ?? input?.method ?? "GET").toUpperCase();
}

function requestPath(input, target) {
  const raw = typeof input === "string" || input instanceof URL ? input : input?.url;
  try {
    return new URL(String(raw), target.location?.origin ?? "http://localhost").pathname;
  } catch {
    return "<invalid-url>";
  }
}

function cacheResult(response) {
  return response.headers.get("cache-status")
    ?? response.headers.get("x-cache")
    ?? response.headers.get("x-dantes-cache")
    ?? "not-reported";
}

function contentLength(response) {
  const raw = response.headers.get("content-length");
  if (raw === null || raw === "") return null;
  const value = Number(raw);
  return Number.isSafeInteger(value) && value >= 0 ? value : null;
}

function boundedPush(values, value, maximum) {
  values.push(value);
  if (values.length > maximum) values.splice(0, values.length - maximum);
}

export function installDevelopmentRequestLedger({ target = globalThis, maximumEntries = 1000 } = {}) {
  if (!Number.isSafeInteger(maximumEntries) || maximumEntries < 1 || maximumEntries > 10000) {
    throw new RangeError("Request ledger limit must be between 1 and 10000.");
  }
  if (target[DEVELOPMENT_OBSERVABILITY_KEY]) return target[DEVELOPMENT_OBSERVABILITY_KEY];
  if (typeof target.fetch !== "function") return null;

  const originalFetch = target.fetch;
  const activeInteractions = [];
  let requestSequence = 0;
  let interactionSequence = 0;
  const state = {
    activeInteractionId: null,
    diagnostics: [],
    interactions: [],
    requests: [],
  };

  const observability = {
    beginInteraction(kind) {
      const id = `${kind}:${++interactionSequence}`;
      const previousId = state.activeInteractionId;
      activeInteractions.push(id);
      state.activeInteractionId = id;
      boundedPush(state.interactions, {
        id,
        kind,
        startedAt: new Date().toISOString(),
      }, maximumEntries);
      return { id, previousId };
    },
    endInteraction(handle) {
      const index = activeInteractions.indexOf(handle.id);
      if (index >= 0) activeInteractions.splice(index, 1);
      state.activeInteractionId = activeInteractions.at(-1) ?? null;
    },
    recordDiagnostic(kind, detail) {
      boundedPush(state.diagnostics, {
        id: `${kind}:${state.diagnostics.length + 1}`,
        parentInteraction: state.activeInteractionId,
        kind,
        detail,
      }, maximumEntries);
    },
    restore() {
      target.fetch = originalFetch;
      delete target[DEVELOPMENT_OBSERVABILITY_KEY];
    },
    snapshot() {
      return JSON.parse(JSON.stringify({
        totalRequests: requestSequence,
        droppedRequests: Math.max(0, requestSequence - state.requests.length),
        diagnostics: state.diagnostics,
        interactions: state.interactions,
        requests: state.requests,
      }));
    },
  };

  target.fetch = async (input, init) => {
    const started = target.performance?.now?.() ?? Date.now();
    const entry = {
      id: ++requestSequence,
      // Overlapping asynchronous interactions have no trustworthy implicit parent.
      parentInteraction: activeInteractions.length === 1 ? state.activeInteractionId : null,
      path: requestPath(input, target),
      method: requestMethod(input, init),
      durationMs: null,
      status: null,
      payloadBytes: null,
      cacheResult: "not-reported",
      outcome: "pending",
    };
    boundedPush(state.requests, entry, maximumEntries);

    try {
      const response = await originalFetch.call(target, input, init);
      entry.durationMs = Math.max(0, (target.performance?.now?.() ?? Date.now()) - started);
      entry.status = response.status;
      entry.payloadBytes = contentLength(response);
      entry.cacheResult = cacheResult(response);
      entry.outcome = "response";
      if (entry.payloadBytes === null && typeof response.clone === "function") {
        // Count transient chunks; never retain or concatenate private response bodies.
        // Stop large/streaming responses so development diagnostics cannot exhaust memory.
        void (async () => {
          const reader = response.clone().body?.getReader();
          if (!reader) { entry.payloadBytes = 0; return; }
          let bytes = 0;
          try {
            while (true) {
              const chunk = await reader.read();
              if (chunk.done) { entry.payloadBytes = bytes; break; }
              bytes += chunk.value.byteLength;
              if (bytes > 8 * 1024 * 1024) { void reader.cancel(); break; }
            }
          } finally { reader.releaseLock(); }
        })().catch(() => {});
      }
      return response;
    } catch (error) {
      entry.durationMs = Math.max(0, (target.performance?.now?.() ?? Date.now()) - started);
      entry.outcome = "network-error";
      throw error;
    }
  };

  target[DEVELOPMENT_OBSERVABILITY_KEY] = observability;
  return observability;
}

export async function withinDevelopmentInteraction(kind, operation, target = globalThis) {
  const observability = target[DEVELOPMENT_OBSERVABILITY_KEY];
  if (!observability) return operation();
  const handle = observability.beginInteraction(kind);
  try {
    return await operation();
  } finally {
    observability.endInteraction(handle);
  }
}

export function recordDevelopmentDiagnostic(kind, detail, target = globalThis) {
  const observability = target[DEVELOPMENT_OBSERVABILITY_KEY];
  if (!observability || typeof observability.recordDiagnostic !== "function") return false;
  observability.recordDiagnostic(kind, detail);
  return true;
}
