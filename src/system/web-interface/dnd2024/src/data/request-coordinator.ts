import { ViewReadError } from "./view-read-client.ts";

export type CoordinatedRequest<TRequest, TValue> = {
  key: (request: TRequest, generation: number) => string;
  read: (request: TRequest, signal: AbortSignal) => Promise<TValue>;
  validate: (value: unknown) => value is TValue;
  maximumBytes: number;
  retryDelayMs?: number;
};

type Flight<T> = { controller: AbortController; consumers: number; settled: boolean; promise: Promise<T> };
const bytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;
const MAX_ACTIVE_FLIGHTS = 16;
const MAX_FLIGHT_KEY_BYTES = 2_048;
const abortError = (cause?: unknown) => new ViewReadError("cancelled", "The request is no longer current.", { cause });
const abortLike = (error: unknown) => error instanceof DOMException && error.name === "AbortError";
const retryable = (error: unknown) => error instanceof TypeError || error instanceof ViewReadError && error.category === "transport";

/** In-flight dedupe/retry/fencing only. Confirmed response values live exclusively in Redux. */
export class RequestCoordinator {
  #generation = 0;
  #scope: string | null = null;
  #flights = new Map<string, Flight<unknown>>();

  replaceScope(scope: string, force = false) {
    if (force || this.#scope !== scope) {
      this.#generation += 1;
      for (const flight of this.#flights.values()) flight.controller.abort();
      this.#flights.clear();
    }
    this.#scope = scope;
  }

  invalidate() {
    this.#generation += 1;
    for (const flight of this.#flights.values()) flight.controller.abort();
    this.#flights.clear();
  }

  load<TRequest, TValue>(definition: CoordinatedRequest<TRequest, TValue>, request: TRequest, signal?: AbortSignal) {
    if (signal?.aborted) return Promise.reject(abortError());
    const generation = this.#generation;
    const key = definition.key(request, generation);
    if (new TextEncoder().encode(key).byteLength > MAX_FLIGHT_KEY_BYTES)
      return Promise.reject(new ViewReadError("incompatible-data", "The request identity exceeded its bound."));
    const active = this.#flights.get(key) as Flight<TValue> | undefined;
    if (active) return this.join(key, active, signal);
    if (this.#flights.size >= MAX_ACTIVE_FLIGHTS)
      return Promise.reject(new ViewReadError("transport", "Too many character reads are already active."));
    const controller = new AbortController();
    const flight: Flight<TValue> = { controller, consumers: 0, settled: false, promise: Promise.resolve(null as never) };
    this.#flights.set(key, flight as Flight<unknown>);
    flight.promise = this.execute(key, generation, definition, request, flight).finally(() => {
      flight.settled = true;
      if (this.#flights.get(key) === flight) this.#flights.delete(key);
    });
    return this.join(key, flight, signal);
  }

  private async execute<TRequest, TValue>(key: string, generation: number,
    definition: CoordinatedRequest<TRequest, TValue>, request: TRequest, flight: Flight<TValue>) {
    let value: unknown;
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try { value = await definition.read(request, flight.controller.signal); break; }
      catch (error) {
        if (flight.controller.signal.aborted || this.#generation !== generation || abortLike(error)) throw abortError(error);
        if (attempt === 0 && retryable(error)) {
          await new Promise<void>((resolve, reject) => {
            const timer = setTimeout(resolve, definition.retryDelayMs ?? 25);
            flight.controller.signal.addEventListener("abort", () => { clearTimeout(timer); reject(abortError()); }, { once: true });
          });
          continue;
        }
        throw error instanceof ViewReadError ? error : new ViewReadError("transport", "The resource could not be read.", { cause: error });
      }
    }
    if (flight.controller.signal.aborted || this.#generation !== generation || this.#flights.get(key) !== flight) throw abortError();
    let valueBytes = Number.MAX_SAFE_INTEGER;
    try { valueBytes = bytes(value); } catch { /* incompatible values are never retained */ }
    if (!definition.validate(value) || valueBytes > definition.maximumBytes)
      throw new ViewReadError("incompatible-data", "The resource response exceeded its contract.");
    return value;
  }

  private join<T>(key: string, flight: Flight<T>, signal?: AbortSignal): Promise<T> {
    flight.consumers += 1;
    return new Promise((resolve, reject) => {
      let done = false;
      const release = () => {
        if (done) return; done = true; signal?.removeEventListener("abort", aborted); flight.consumers -= 1;
        if (!flight.consumers && !flight.settled && this.#flights.get(key) === flight) {
          flight.controller.abort(); this.#flights.delete(key);
        }
      };
      const aborted = () => { release(); reject(abortError()); };
      signal?.addEventListener("abort", aborted, { once: true });
      flight.promise.then((value) => { if (!done) { release(); resolve(value); } }, (error) => { if (!done) { release(); reject(error); } });
    });
  }
}
