import type { Perspective, TacticalEncounterBoard, VisualMedia } from "../data/hub-types";
import type { BoardDraftScope } from "../server/board-draft";
import { BoardDraftWorkshop } from "./BoardDraftWorkshop";
import { TacticalBoard } from "./TacticalBoard";

// Display-only placeholder, never a proposed board or a source of token placement/scale.
const EMPTY_GRID: TacticalEncounterBoard = {
  revision: 0, columns: 20, rows: 20, feetPerSquare: 5,
  terrain: [], obstacles: [], participants: [],
};

export function CombatBoard({ board, perspective, background, draftScope, onAccepted }: { board?: TacticalEncounterBoard; perspective: Perspective; background?: VisualMedia; draftScope?: BoardDraftScope; onAccepted?: () => void }) {
  return <>
    {!board ? <aside className="tactical-board-fallback" aria-label="Board unavailable">
      <h2>No accepted board is available to this view</h2>
      <p>This empty grid is only a visual placeholder. It does not establish terrain, distances, obstacles, or combatant positions. Continue using the Initiative list.</p>
      {perspective === "dm" && !draftScope ? <>
        <button type="button" disabled aria-describedby="board-generation-unavailable">Generate combat map</button>
        <p id="board-generation-unavailable">Map generation and draft review are not available yet. No board will be created or accepted here.</p>
      </> : null}
    </aside> : null}
    <TacticalBoard board={board ?? EMPTY_GRID} placeholder={!board} background={background} />
    {perspective === "dm" && draftScope && onAccepted ? <BoardDraftWorkshop key={`${draftScope.encounterId}:${board?.revision ?? 0}`} scope={draftScope} onAccepted={onAccepted} /> : null}
  </>;
}
