import type { WorldLoreEntry } from "./hub-types";

export const LORE_CATEGORIES: Record<string, string> = {
  event: "History & events", location: "Places & geography", identity: "People & identities",
  relationship: "Relationships", intention: "Plans & intentions", rule: "Rules & customs",
  capability: "Abilities & capabilities", quantity: "Quantities", state: "Current state", negative: "Absences",
};
export const LORE_KINDS: Record<string, string> = { fact: "Facts", rumour: "Rumours", secret: "Secrets", clue: "Clues" };

export type LorePageRequest = {
  query: string; category: string; kind: string; cursor: string | null;
  expectedSourceRevision?: string | null;
  expectedGraphRevision?: string | null;
  expectedSelectionFingerprint?: string | null;
};
export type WorldLorePage = {
  entries: WorldLoreEntry[];
  totalCount: number | null;
  nextCursor: string | null;
  sourceRevision: string;
  graphRevision: string;
  selectionFingerprint: string | null;
  facets: { category: Record<string, number>; kind: Record<string, number> };
  coverage: "complete" | "partial";
  filterable: boolean;
};
export type LorePageLoader = (request: LorePageRequest, signal: AbortSignal, preferCached?: boolean) => Promise<WorldLorePage>;
