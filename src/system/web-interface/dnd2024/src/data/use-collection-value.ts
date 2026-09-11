import { useCallback, useContext, useMemo, useRef, useState, useSyncExternalStore, type SetStateAction } from "react";
import { ReactReduxContext } from "react-redux";
import type { HubStore } from "./hub-store";
import { collectionActions, MAX_COLLECTION_ENTRY_BYTES } from "./collection-state";
import { ViewReadError } from "./view-read-client";

const idleSubscribe = () => () => {};
/**
 * Completed directory/definition/container facets live in the existing Hub store.
 * Each mounted consumer holds only its selection and request controls. Facets
 * without complete component provenance stay separate rather than being merged
 * into a guessed ECS record. The local path supports standalone presentation fixtures.
 */
export function useCollectionValue<T>(key: string, initial: T, maximumAgeMs = 60_000) {
  const context = useContext(ReactReduxContext);
  const store = context?.store as HubStore | undefined;
  const [local, setLocal] = useState(initial);
  const empty = useMemo(() => initial, [key]); // Initial shape, never a completed response.
  const getEpoch = useCallback(() => JSON.stringify([store?.getState().collections.scope, store?.getState().collections.generation]), [store]);
  const epoch = useSyncExternalStore(store?.subscribe ?? idleSubscribe, getEpoch, getEpoch);
  const getEntry = useCallback(() => store?.getState().collections.entries[key], [key, store]);
  const entry = useSyncExternalStore(store?.subscribe ?? idleSubscribe, getEntry, getEntry);
  // Keep only lifecycle metadata locally. Eviction must be distinguishable from
  // an authoritative empty result without retaining a second copy of the body.
  const observed = useRef({ key, epoch, present: Boolean(entry) });
  if (observed.current.key !== key || observed.current.epoch !== epoch)
    observed.current = { key, epoch, present: Boolean(entry) };
  else if (entry) observed.current.present = true;
  const evicted = Boolean(store && !entry && observed.current.present);
  const scope = store?.getState().collections.scope ?? null, generation = store?.getState().collections.generation ?? 0;
  const value = entry?.value as T | undefined;
  const setValue = useCallback((update: SetStateAction<T>) => {
    if (!store) { setLocal(update); return; }
    const current = store.getState().collections;
    if (current.scope !== scope || current.generation !== generation || current.denied) return;
    const previous = current.entries[key]?.value as T | undefined;
    const next = typeof update === "function" ? (update as (value: T) => T)(previous ?? empty) : update;
    const bytes = new TextEncoder().encode(JSON.stringify(next)).byteLength;
    if (bytes > MAX_COLLECTION_ENTRY_BYTES)
      throw new ViewReadError("incompatible-data", "The collection exceeded its memory contract.");
    store.dispatch(collectionActions.committed({ scope, generation, key, value: next, bytes, confirmedAt: Date.now() }));
  }, [empty, generation, key, scope, store]);
  const deny = useCallback(() => {
    if (store) store.dispatch(collectionActions.denied({ scope, generation }));
    else setLocal(empty);
  }, [empty, generation, scope, store]);
  return [store ? value ?? empty : local, setValue, epoch,
    Boolean(entry && Date.now() - entry.confirmedAt < maximumAgeMs),
    !store || scope !== null && !store.getState().collections.denied, deny, evicted] as const;
}
