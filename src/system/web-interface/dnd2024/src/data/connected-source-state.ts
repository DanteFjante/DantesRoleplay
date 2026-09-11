import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

import type { ConnectedCampaignEnvelope } from "./hub-types";

/**
 * The authorized bootstrap input for deferred projections. This belongs to the
 * hub store so it has the same lifetime, inspection path, and scope boundary
 * as every other confirmed hub input. The workspace owns only request leases.
 */
export type ConnectedSourceState = {
  scope: string | null;
  value: ConnectedCampaignEnvelope | null;
  retainedBytes: number;
};

const initialState: ConnectedSourceState = { scope: null, value: null, retainedBytes: 0 };

const source = createSlice({
  name: "connectedSource",
  initialState,
  reducers: {
    replaced(state, action: PayloadAction<{ scope: string; value: ConnectedCampaignEnvelope; bytes: number }>) {
      state.scope = action.payload.scope;
      state.value = action.payload.value;
      state.retainedBytes = action.payload.bytes;
    },
    updated(state, action: PayloadAction<{ scope: string; value: ConnectedCampaignEnvelope; bytes: number }>) {
      if (state.scope !== action.payload.scope) return;
      state.value = action.payload.value;
      state.retainedBytes = action.payload.bytes;
    },
    cleared(state) {
      state.scope = null;
      state.value = null;
      state.retainedBytes = 0;
    },
  },
  // The hub's existing scope clear actions must not leave an authorized raw
  // input behind if a caller dispatches them directly rather than through a
  // resource-owner callback. These are local slice action names, not a new
  // public protocol surface.
  extraReducers: (builder) => {
    for (const type of ["confirmed/scopeCleared", "table/scopeCleared"])
      builder.addCase(type, (state) => {
        state.scope = null;
        state.value = null;
        state.retainedBytes = 0;
      });
  },
});

export const connectedSourceActions = source.actions;
export const connectedSourceReducer = source.reducer;

export function selectConnectedSource(state: { connectedSource: ConnectedSourceState }, scope: string) {
  return state.connectedSource.scope === scope ? state.connectedSource.value : null;
}
