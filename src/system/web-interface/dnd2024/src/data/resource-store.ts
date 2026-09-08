import type { ResourceFailure, ResourceResult, ResourceState } from "./resource-state.ts";
import { ViewReadError } from "./view-read-client.ts";

type ResourceRead<TRequest, TResponse> = (
  request: TRequest,
  signal: AbortSignal,
) => Promise<TResponse>;

type ResourceDefinition<TRequest, TResponse> = {
  name: string;
  cacheKey: (request: TRequest) => string;
  read: ResourceRead<TRequest, TResponse>;
  validate: (value: unknown) => value is TResponse;
  maximumAgeMs?: number;
  maximumEntryBytes?: number;
  retryDelayMs?: number;
};

type StoredEntry<T> = {
  state: ResourceState<T>;
  result?: ResourceResult<T>;
  bytes: number;
  storedAt: number;
  lastAccess: number;
};

type InFlight<T> = {
  controller: AbortController;
  consumers: number;
  settled: boolean;
  previous?: StoredEntry<T>;
  promise: Promise<ResourceResult<T>>;
};

type ResourceLoadOptions = {
  preferCached?: boolean;
  signal?: AbortSignal;
};

type ResourceStoreOptions = {
  maximumEntries?: number;
  maximumRetainedBytes?: number;
};

const unloaded = <T>(): ResourceState<T> => ({ status: "unloaded", data: null });

function cancelledError(cause?: unknown) {
  return new ViewReadError("cancelled", "The resource request is no longer active.", { cause });
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

function isRetryable(error: unknown) {
  return error instanceof TypeError ||
    (error instanceof ViewReadError && error.category === "transport");
}

function failure(error: ViewReadError): ResourceFailure {
  return {
    category: error.category === "incompatible-data" ? "incompatible-data"
      : error.category === "stale-data" ? "stale-data" : "transport",
    message: error.message,
  };
}

async function fingerprint(value: unknown): Promise<string> {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0"))
    .join("").toUpperCase();
}

function encodedBytes(value: unknown) {
  return new TextEncoder().encode(JSON.stringify(value)).byteLength;
}

/**
 * One bounded browser owner for independently loading resources. Entries are isolated by
 * resource name and normalized key. Same-key consumers share work; invalidation aborts and
 * fences late completion. Response bodies remain in memory only.
 */
export class ResourceStore {
  readonly #maximumEntries: number;
  readonly #maximumRetainedBytes: number;
  readonly #entries = new Map<string, StoredEntry<unknown>>();
  readonly #inFlight = new Map<string, InFlight<unknown>>();
  readonly #listeners = new Map<string, Set<(state: ResourceState<unknown>) => void>>();
  readonly #resourceNames = new Set<string>();
  #requestId = 0;

  constructor(options: ResourceStoreOptions = {}) {
    this.#maximumEntries = options.maximumEntries ?? 16;
    this.#maximumRetainedBytes = options.maximumRetainedBytes ?? 4 * 1024 * 1024;
    if (!Number.isSafeInteger(this.#maximumEntries) || this.#maximumEntries < 1 ||
        !Number.isSafeInteger(this.#maximumRetainedBytes) || this.#maximumRetainedBytes < 1) {
      throw new Error("Resource retention limits must be positive integers.");
    }
  }

  define<TRequest, TResponse>(definition: ResourceDefinition<TRequest, TResponse>) {
    if (!/^[a-z][a-z0-9-]{0,79}$/u.test(definition.name) || this.#resourceNames.has(definition.name))
      throw new Error("Resource names must be unique stable identifiers.");
    this.#resourceNames.add(definition.name);
    return new KeyedResource<TRequest, TResponse>(this, definition);
  }

  invalidateAll() {
    for (const key of new Set([...this.#entries.keys(), ...this.#inFlight.keys()])) this.invalidateKey(key);
  }

  async load<TRequest, TResponse>(
    definition: ResourceDefinition<TRequest, TResponse>,
    request: TRequest,
    options: ResourceLoadOptions,
  ): Promise<ResourceResult<TResponse>> {
    if (options.signal?.aborted) throw cancelledError();
    const key = this.key(definition, request);
    if (options.preferCached) {
      const cached = this.peek(definition, request);
      if (cached) return cached;
    }
    const existing = this.#inFlight.get(key) as InFlight<TResponse> | undefined;
    if (existing) return this.join(key, existing, options.signal);

    const previous = this.#entries.get(key) as StoredEntry<TResponse> | undefined;
    const controller = new AbortController();
    const flight = {
      controller, consumers: 0, settled: false, previous,
      promise: Promise.resolve(null as unknown as ResourceResult<TResponse>),
    };
    this.#inFlight.set(key, flight as InFlight<unknown>);
    this.storeState(key, {
      state: { status: "loading", data: previous?.result?.value ?? null },
      result: previous?.result,
      bytes: previous?.bytes ?? 0,
      storedAt: previous?.storedAt ?? Date.now(),
      lastAccess: Date.now(),
    });
    flight.promise = this.execute(key, definition, request, flight).finally(() => {
      flight.settled = true;
      if (this.#inFlight.get(key) === flight) {
        this.#inFlight.delete(key);
        this.trim();
      }
    });
    return this.join(key, flight, options.signal);
  }

  peek<TRequest, TResponse>(
    definition: ResourceDefinition<TRequest, TResponse>,
    request: TRequest,
  ): ResourceResult<TResponse> | null {
    const key = this.key(definition, request);
    const entry = this.#entries.get(key) as StoredEntry<TResponse> | undefined;
    const maximumAgeMs = definition.maximumAgeMs ?? 30_000;
    if (entry?.result && Date.now() - entry.storedAt >= maximumAgeMs) {
      this.invalidateKey(key);
      return null;
    }
    if (!entry?.result) return null;
    entry.lastAccess = Date.now();
    return entry.result;
  }

  state<TRequest, TResponse>(
    definition: ResourceDefinition<TRequest, TResponse>,
    request: TRequest,
  ): ResourceState<TResponse> {
    const key = this.key(definition, request);
    this.peek(definition, request);
    return (this.#entries.get(key)?.state as ResourceState<TResponse> | undefined) ?? unloaded();
  }

  subscribe<TRequest, TResponse>(
    definition: ResourceDefinition<TRequest, TResponse>,
    request: TRequest,
    listener: (state: ResourceState<TResponse>) => void,
  ) {
    const key = this.key(definition, request);
    const listeners = this.#listeners.get(key) ?? new Set();
    listeners.add(listener as (state: ResourceState<unknown>) => void);
    this.#listeners.set(key, listeners);
    listener(this.state(definition, request));
    return () => {
      listeners.delete(listener as (state: ResourceState<unknown>) => void);
      if (listeners.size === 0) this.#listeners.delete(key);
    };
  }

  invalidate<TRequest, TResponse>(
    definition: ResourceDefinition<TRequest, TResponse>,
    request?: TRequest,
  ) {
    if (request !== undefined) {
      this.invalidateKey(this.key(definition, request));
      return;
    }
    const prefix = `${definition.name}\u0000`;
    for (const key of new Set([...this.#entries.keys(), ...this.#inFlight.keys()]))
      if (key.startsWith(prefix)) this.invalidateKey(key);
  }

  private key<TRequest, TResponse>(definition: ResourceDefinition<TRequest, TResponse>, request: TRequest) {
    const part = definition.cacheKey(request);
    if (!part || part.length > 4_096 || /[\u0000-\u001F\u007F]/u.test(part))
      throw new Error("Resource cache keys must be bounded printable text.");
    return `${definition.name}\u0000${part}`;
  }

  private async execute<TRequest, TResponse>(
    key: string,
    definition: ResourceDefinition<TRequest, TResponse>,
    request: TRequest,
    flight: InFlight<TResponse>,
  ): Promise<ResourceResult<TResponse>> {
    try {
      let value: unknown;
      for (let attempt = 0; attempt < 2; attempt += 1) {
        try {
          value = await definition.read(request, flight.controller.signal);
          break;
        } catch (error) {
          if (flight.controller.signal.aborted || isAbortError(error) || this.#inFlight.get(key) !== flight)
            throw cancelledError(error);
          if (attempt === 0 && isRetryable(error)) {
            await this.delay(definition.retryDelayMs ?? 25, flight.controller.signal);
            continue;
          }
          throw error instanceof ViewReadError ? error
            : new ViewReadError("transport", "The resource could not be read.", { cause: error });
        }
      }
      if (this.#inFlight.get(key) !== flight || flight.controller.signal.aborted) throw cancelledError();
      if (!definition.validate(value))
        throw new ViewReadError("incompatible-data", "The resource response did not match its contract.");
      const bytes = encodedBytes(value);
      if (bytes > (definition.maximumEntryBytes ?? this.#maximumRetainedBytes))
        throw new ViewReadError("incompatible-data", "The resource response exceeded its memory contract.");
      const result = { requestId: ++this.#requestId, fingerprint: await fingerprint(value), value };
      if (this.#inFlight.get(key) !== flight || flight.controller.signal.aborted) throw cancelledError();
      const now = Date.now();
      this.storeState(key, { state: { status: "ready", data: value, result }, result,
        bytes, storedAt: now, lastAccess: now });
      return result;
    } catch (error) {
      if (flight.controller.signal.aborted || this.#inFlight.get(key) !== flight || isAbortError(error))
        throw error instanceof ViewReadError && error.category === "cancelled" ? error : cancelledError(error);
      const typed = error instanceof ViewReadError ? error
        : new ViewReadError("transport", "The resource could not be read.", { cause: error });
      const detail = failure(typed);
      if (flight.previous?.result) {
        this.storeState(key, { ...flight.previous,
          state: { status: "stale", data: flight.previous.result.value,
            result: flight.previous.result, failure: detail }, lastAccess: Date.now() });
      } else {
        this.storeState(key, { state: {
          status: typed.category === "incompatible-data" ? "incompatible" : "error",
          data: null, failure: detail,
        }, bytes: 0, storedAt: Date.now(), lastAccess: Date.now() });
      }
      throw typed;
    }
  }

  private join<T>(key: string, flight: InFlight<T>, signal?: AbortSignal): Promise<ResourceResult<T>> {
    flight.consumers += 1;
    return new Promise((resolve, reject) => {
      let finished = false;
      const release = () => {
        if (finished) return;
        finished = true;
        signal?.removeEventListener("abort", abort);
        flight.consumers -= 1;
        if (flight.consumers === 0 && !flight.settled && this.#inFlight.get(key) === flight)
          this.abandonKey(key, flight);
      };
      const abort = () => { release(); reject(cancelledError()); };
      signal?.addEventListener("abort", abort, { once: true });
      flight.promise.then((result) => { if (!finished) { release(); resolve(result); } },
        (error) => { if (!finished) { release(); reject(error); } });
    });
  }

  private abandonKey<T>(key: string, flight: InFlight<T>) {
    flight.controller.abort();
    this.#inFlight.delete(key);
    if (flight.previous) this.storeState(key, flight.previous);
    else { this.#entries.delete(key); this.notify(key, unloaded()); }
  }

  private invalidateKey(key: string) {
    const flight = this.#inFlight.get(key);
    if (flight) { flight.controller.abort(); this.#inFlight.delete(key); }
    this.#entries.delete(key);
    this.notify(key, unloaded());
  }

  private storeState<T>(key: string, entry: StoredEntry<T>) {
    this.#entries.delete(key);
    this.#entries.set(key, entry as StoredEntry<unknown>);
    this.notify(key, entry.state);
    this.trim();
  }

  private trim() {
    const totalBytes = () => [...this.#entries.values()].reduce((total, entry) => total + entry.bytes, 0);
    while (this.#entries.size > this.#maximumEntries || totalBytes() > this.#maximumRetainedBytes) {
      const candidate = [...this.#entries.entries()]
        .filter(([key]) => !this.#inFlight.has(key))
        .sort(([, left], [, right]) => left.lastAccess - right.lastAccess)[0];
      if (!candidate) break;
      this.#entries.delete(candidate[0]);
      this.notify(candidate[0], unloaded());
    }
  }

  private notify<T>(key: string, state: ResourceState<T>) {
    for (const listener of this.#listeners.get(key) ?? []) listener(state as ResourceState<unknown>);
  }

  private delay(milliseconds: number, signal: AbortSignal) {
    return new Promise<void>((resolve, reject) => {
      const finish = () => { signal.removeEventListener("abort", abort); resolve(); };
      const timer = globalThis.setTimeout(finish, milliseconds);
      const abort = () => {
        globalThis.clearTimeout(timer);
        reject(cancelledError());
      };
      signal.addEventListener("abort", abort, { once: true });
    });
  }
}

export class KeyedResource<TRequest, TResponse> {
  private readonly store: ResourceStore;
  private readonly definition: ResourceDefinition<TRequest, TResponse>;

  constructor(
    store: ResourceStore,
    definition: ResourceDefinition<TRequest, TResponse>,
  ) {
    this.store = store;
    this.definition = definition;
  }

  load(request: TRequest, options: ResourceLoadOptions = {}) {
    return this.store.load(this.definition, request, options);
  }

  peek(request: TRequest) {
    return this.store.peek(this.definition, request);
  }

  state(request: TRequest) {
    return this.store.state(this.definition, request);
  }

  subscribe(request: TRequest, listener: (state: ResourceState<TResponse>) => void) {
    return this.store.subscribe(this.definition, request, listener);
  }

  invalidate(request?: TRequest) {
    this.store.invalidate(this.definition, request);
  }
}
