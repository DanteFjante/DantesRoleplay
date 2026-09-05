export type ViewReadErrorCategory = "cancelled" | "incompatible-data" | "transport";

export class ViewReadError extends Error {
  constructor(
    public readonly category: ViewReadErrorCategory,
    message: string,
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = "ViewReadError";
  }
}

type ViewReadOperation<TRequest, TResponse> = (
  request: TRequest,
  signal: AbortSignal,
) => Promise<TResponse>;

type ViewReadClientOptions<TRequest, TResponse> = {
  read: ViewReadOperation<TRequest, TResponse>;
  cacheKey: (request: TRequest) => string;
  validate: (value: unknown) => value is TResponse;
  retryDelayMs?: number;
  maximumCachedScopes?: number;
  maximumCacheAgeMs?: number;
};

export type ViewReadResult<TResponse> = {
  requestId: number;
  fingerprint: string;
  value: TResponse;
};

type CacheEntry<TResponse> = ViewReadResult<TResponse> & {
  scopeKey: string;
  storedAt: number;
};

function cancelledError(cause?: unknown) {
  return new ViewReadError("cancelled", "A newer view request replaced this request.", { cause });
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

function isRetryable(error: unknown) {
  return error instanceof TypeError ||
    (error instanceof ViewReadError && error.category === "transport");
}

async function sha256(value: unknown): Promise<string> {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
}

/**
 * Coordinates private view reads without persisting response bodies. A new request always aborts
 * the previous request, and the monotonically increasing request id is rechecked even when a
 * supplied reader ignores AbortSignal. Only validated, current responses enter the memory cache.
 */
export class ViewReadClient<TRequest, TResponse> {
  readonly #read: ViewReadOperation<TRequest, TResponse>;
  readonly #cacheKey: (request: TRequest) => string;
  readonly #validate: (value: unknown) => value is TResponse;
  readonly #retryDelayMs: number;
  readonly #maximumCachedScopes: number;
  readonly #maximumCacheAgeMs: number;
  readonly #cache = new Map<string, CacheEntry<TResponse>>();
  #requestId = 0;
  #controller: AbortController | null = null;

  constructor(options: ViewReadClientOptions<TRequest, TResponse>) {
    this.#read = options.read;
    this.#cacheKey = options.cacheKey;
    this.#validate = options.validate;
    this.#retryDelayMs = options.retryDelayMs ?? 25;
    this.#maximumCachedScopes = options.maximumCachedScopes ?? 8;
    this.#maximumCacheAgeMs = options.maximumCacheAgeMs ?? 30_000;
  }

  async load(request: TRequest): Promise<ViewReadResult<TResponse>> {
    const requestId = ++this.#requestId;
    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    const scopeKey = this.#cacheKey(request);

    let value: unknown;
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        value = await this.#read(request, controller.signal);
        break;
      } catch (error) {
        if (controller.signal.aborted || isAbortError(error) || requestId !== this.#requestId) {
          throw cancelledError(error);
        }
        if (attempt === 0 && isRetryable(error)) {
          await new Promise<void>((resolve, reject) => {
            const timer = globalThis.setTimeout(resolve, this.#retryDelayMs);
            controller.signal.addEventListener("abort", () => {
              globalThis.clearTimeout(timer);
              reject(cancelledError());
            }, { once: true });
          });
          continue;
        }
        throw error instanceof ViewReadError
          ? error
          : new ViewReadError("transport", "The view could not be read.", { cause: error });
      }
    }

    if (controller.signal.aborted || requestId !== this.#requestId) throw cancelledError();
    if (!this.#validate(value)) {
      throw new ViewReadError("incompatible-data", "The view response did not match its contract.");
    }

    const fingerprint = await sha256(value);
    if (controller.signal.aborted || requestId !== this.#requestId) throw cancelledError();
    const result = { requestId, fingerprint, value };
    this.#remember({ ...result, scopeKey, storedAt: Date.now() });
    return result;
  }

  peek(request: TRequest): ViewReadResult<TResponse> | null {
    const entry = this.#cache.get(this.#cacheKey(request));
    if (entry && Date.now() - entry.storedAt >= this.#maximumCacheAgeMs) {
      this.#cache.delete(entry.scopeKey);
      return null;
    }
    return entry
      ? { requestId: entry.requestId, fingerprint: entry.fingerprint, value: entry.value }
      : null;
  }

  invalidate(request?: TRequest) {
    this.cancel();
    if (request === undefined) {
      this.#cache.clear();
      return;
    }
    this.#cache.delete(this.#cacheKey(request));
  }

  cancel() {
    this.#requestId += 1;
    this.#controller?.abort();
    this.#controller = null;
  }

  #remember(entry: CacheEntry<TResponse>) {
    this.#cache.delete(entry.scopeKey);
    this.#cache.set(entry.scopeKey, entry);
    while (this.#cache.size > this.#maximumCachedScopes) {
      const oldest = this.#cache.keys().next().value;
      if (oldest === undefined) break;
      this.#cache.delete(oldest);
    }
  }
}
