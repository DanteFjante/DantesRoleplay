/** One view load owns its queue and listing cache; private responses never cross view loads. */
export function createHubReadScope(fetchImpl) {
  const queue = [];
  const listings = new Map();
  let active = 0;
  let cachedBytes = 0;
  let failure = null;

  function drain() {
    while (active < 8 && queue.length > 0) {
      const { run, resolve, reject } = queue.shift();
      if (failure) { reject(new Error(failure)); continue; }
      active += 1;
      Promise.resolve().then(run).then(resolve, reject).finally(() => { active -= 1; drain(); });
    }
  }

  async function read(input, init = {}) {
    if (failure) throw new Error(failure);
    if (init.signal?.aborted) throw new DOMException("View replaced", "AbortError");
    const target = new URL(String(input));
    const isListing = (init.method ?? "GET") === "GET" &&
      /\/state-spaces\/[^/]+\/entities$/u.test(target.pathname);
    const isHierarchy = isListing || target.pathname.endsWith("/containment") ||
      target.pathname.endsWith("/components/game.core.world.location");
    const key = target.href;
    if (isListing && listings.has(key)) {
      const result = await listings.get(key);
      return typeof result.clone === "function" ? result.clone() : result;
    }
    if (queue.length >= 2_048) {
      failure = "The world view is too large to load safely. Your current view has been kept.";
      throw new Error(failure);
    }
    const response = new Promise((resolve, reject) => {
      queue.push({ resolve, reject, run: async () => {
        if (init.signal?.aborted) throw new DOMException("View replaced", "AbortError");
        let result;
        try { result = await fetchImpl(input, init); }
        catch (error) {
          if (isHierarchy && !init.signal?.aborted)
            failure = "Some world information could not be loaded. Please try again; your current view has been kept.";
          throw error;
        }
        if (result.status === 429) {
          failure = "The server is busy. Please wait a moment and try again; your current view has been kept.";
        } else if (isHierarchy && result.status >= 500) {
          failure = "Some world information could not be loaded. Please try again; your current view has been kept.";
        }
        return result;
      } });
      drain();
    });
    if (!isListing || listings.size >= 32) return response;
    // Only successful, bounded JSON listings are reused within this one authorized read.
    const retained = response.then(async (result) => {
      if (!result.ok || typeof result.clone !== "function") { listings.delete(key); return result; }
      const body = await result.clone().text();
      const size = body.length * 2;
      if (size > 128 * 1024 || cachedBytes + size > 2 * 1024 * 1024) listings.delete(key);
      else cachedBytes += size;
      return result;
    }, (error) => { listings.delete(key); throw error; });
    listings.set(key, retained);
    const result = await retained;
    return typeof result.clone === "function" ? result.clone() : result;
  }

  return { fetch: read, get failure() { return failure; } };
}
