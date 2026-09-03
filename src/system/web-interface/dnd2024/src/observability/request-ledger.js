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
  if (target[DEVELOPMENT_OBSERVABILITY_KEY]) return target[DEVELOPMENT_OBSERVABILITY_KEY];
  if (typeof target.fetch !== "function") return null;

  const originalFetch = target.fetch.bind(target);
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
      state.activeInteractionId = id;
      boundedPush(state.interactions, {
        id,
        kind,
        startedAt: new Date().toISOString(),
      }, maximumEntries);
      return { id, previousId };
    },
    endInteraction(handle) {
      if (state.activeInteractionId === handle.id) state.activeInteractionId = handle.previousId;
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
      parentInteraction: state.activeInteractionId,
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
      const response = await originalFetch(input, init);
      entry.durationMs = Math.max(0, (target.performance?.now?.() ?? Date.now()) - started);
      entry.status = response.status;
      entry.payloadBytes = contentLength(response);
      entry.cacheResult = cacheResult(response);
      entry.outcome = "response";
      if (entry.payloadBytes === null && typeof response.clone === "function") {
        void response.clone().arrayBuffer()
          .then((payload) => { entry.payloadBytes = payload.byteLength; })
          .catch(() => {});
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
