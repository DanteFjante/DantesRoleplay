import type { ResourceEdit } from "./resource-state";
import { reduceResourceEdits } from "./resource-state";

export type HubObjectUiState = {
  selectedFactionId: string;
  edits: Record<string, ResourceEdit>;
};

export type HubObjectUiAction =
  | { type: "faction-selected"; factionId: string }
  | { type: "scope-replaced"; factionId: string }
  | { type: "edit-staged"; objectId: string; draft: unknown }
  | { type: "edit-cancelled"; objectId: string }
  | { type: "write-submitted"; objectId: string }
  | { type: "write-failed"; objectId: string; error: string }
  | { type: "write-confirmed"; objectId: string };

export function createHubObjectUiState(selectedFactionId: string): HubObjectUiState {
  return { selectedFactionId, edits: {} };
}

/** Selection is shell state; edits delegate to the shared non-authoritative draft reducer. */
export function hubObjectUiReducer(state: HubObjectUiState, action: HubObjectUiAction): HubObjectUiState {
  switch (action.type) {
    case "faction-selected":
      return action.factionId === state.selectedFactionId ? state : { ...state, selectedFactionId: action.factionId };
    case "scope-replaced":
      return createHubObjectUiState(action.factionId);
    default: {
      const edits = reduceResourceEdits(state.edits, { ...action, resourceKey: action.objectId });
      return edits === state.edits ? state : { ...state, edits };
    }
  }
}
