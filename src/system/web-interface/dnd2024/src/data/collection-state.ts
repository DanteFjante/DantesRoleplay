import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

type Entry = { value: unknown; bytes: number; revision: number; confirmedAt: number };
export type CollectionState = { scope: string | null; generation: number; revision: number; denied: boolean;
  entries: Record<string, Entry>; retainedBytes: number };
export const MAX_COLLECTION_BYTES = 16 * 1024 * 1024;
export const MAX_COLLECTION_ENTRY_BYTES = 10 * 1024 * 1024;
export const MAX_COLLECTION_ENTRIES = 64;
const initialState: CollectionState = { scope: null, generation: 0, revision: 0, denied: false, entries: {}, retainedBytes: 0 };
const collections = createSlice({ name: "collections", initialState, reducers: {
  scopeReplaced(state, action: PayloadAction<{ scope: string | null; generation: number }>) {
    if (state.scope === action.payload.scope && state.generation === action.payload.generation) return;
    state.scope = action.payload.scope; state.generation = action.payload.generation;
    state.entries = {}; state.retainedBytes = 0; state.denied = false;
  },
  committed(state, action: PayloadAction<{ scope: string | null; generation: number; key: string; value: unknown; bytes: number; confirmedAt: number }>) {
    const { scope, generation, key, value, bytes, confirmedAt } = action.payload;
    if (!scope || state.denied || state.scope !== scope || state.generation !== generation || !key.length || key.length > 2048 ||
        ["__proto__", "prototype", "constructor"].includes(key) ||
        !Number.isSafeInteger(bytes) || bytes < 0 || bytes > MAX_COLLECTION_ENTRY_BYTES || !Number.isFinite(confirmedAt)) return;
    // Account for the actual JSON payload, not caller-supplied size metadata.
    // This is a transport bound, not a schema for the composed object's fields.
    try {
      const json = JSON.stringify(value);
      if (typeof json !== "string" || new TextEncoder().encode(json).byteLength !== bytes) return;
    } catch { return; }
    state.retainedBytes -= state.entries[key]?.bytes ?? 0;
    state.entries[key] = { value, bytes, revision: ++state.revision, confirmedAt };
    state.retainedBytes += bytes;
    while (Object.keys(state.entries).length > MAX_COLLECTION_ENTRIES || state.retainedBytes > MAX_COLLECTION_BYTES) {
      const oldest = Object.entries(state.entries).sort(([, a], [, b]) => a.revision - b.revision)[0];
      if (!oldest) break;
      state.retainedBytes -= oldest[1].bytes; delete state.entries[oldest[0]];
    }
  },
  denied(state, action: PayloadAction<{ scope: string | null; generation: number }>) {
    if (state.scope !== action.payload.scope || state.generation !== action.payload.generation) return;
    state.denied = true; state.generation++; state.entries = {}; state.retainedBytes = 0;
  },
  released(state, action: PayloadAction<{ scope: string | null; generation: number; key: string }>) {
    const { scope, generation, key } = action.payload;
    if (state.scope !== scope || state.generation !== generation) return;
    state.retainedBytes -= state.entries[key]?.bytes ?? 0; delete state.entries[key];
  },
} });
export const collectionActions = collections.actions;
export const collectionReducer = collections.reducer;
