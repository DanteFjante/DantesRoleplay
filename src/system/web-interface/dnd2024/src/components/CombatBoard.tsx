import type { Perspective, TacticalEncounterBoard } from "../data/hub-types";
import { TacticalBoard } from "./TacticalBoard";

// Display-only placeholder, never a proposed board or a source of token placement/scale.
const EMPTY_GRID: TacticalEncounterBoard = {
  revision: 0, columns: 20, rows: 20, feetPerSquare: 5,
  terrain: [], obstacles: [], participants: [],
};

export function CombatBoard({ board, perspective }: { board?: TacticalEncounterBoard; perspective: Perspective }) {
  return <>
    {!board ? <aside className="tactical-board-fallback" aria-label="Board unavailable">
      <h2>No accepted board is available to this view</h2>
      <p>This empty grid is only a visual placeholder. It does not establish terrain, distances, obstacles, or combatant positions. Continue using the Initiative list.</p>
      {perspective === "dm" ? <>
        <button type="button" disabled aria-describedby="board-generation-unavailable">Generate combat map</button>
        <p id="board-generation-unavailable">Map generation and draft review are not available yet. No board will be created or accepted here.</p>
      </> : null}
    </aside> : null}
    <TacticalBoard board={board ?? EMPTY_GRID} placeholder={!board} />
  </>;
}
