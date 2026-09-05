import {
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  useRef,
  useState,
} from "react";
import type { TacticalEncounterBoard } from "../data/hub-types";
import { Icon } from "./Icon";

type Viewport = { zoom: number; x: number; y: number };
type Point = { x: number; y: number };
type Drag = { pointerId: number; point: Point; view: Viewport };

const DEFAULT_VIEW: Viewport = { zoom: 1, x: 0, y: 0 };
const MIN_ZOOM = 0.5;
const MAX_ZOOM = 4;
const ZOOM_STEP = 0.25;
const PAN_STEP = 64;

function clampZoom(value: number) {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
}

function placement(
  area: { x: number; y: number; width: number; height: number },
  board: TacticalEncounterBoard,
): CSSProperties {
  return {
    left: `${(area.x / board.columns) * 100}%`,
    top: `${(area.y / board.rows) * 100}%`,
    width: `${(area.width / board.columns) * 100}%`,
    height: `${(area.height / board.rows) * 100}%`,
  };
}

export function TacticalBoard({ board }: { board: TacticalEncounterBoard }) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<Drag | null>(null);
  const [viewport, setViewport] = useState(DEFAULT_VIEW);
  const [selectedId, setSelectedId] = useState("");

  const constrain = (candidate: Viewport): Viewport => {
    const element = viewportRef.current;
    const stage = stageRef.current;
    const zoom = clampZoom(candidate.zoom);
    if (!element || !stage) return { ...candidate, zoom };
    const width = stage.offsetWidth * zoom;
    const height = stage.offsetHeight * zoom;
    const axis = (value: number, viewportSize: number, contentSize: number) =>
      contentSize <= viewportSize
        ? (viewportSize - contentSize) / 2
        : Math.min(0, Math.max(viewportSize - contentSize, value));
    return {
      zoom,
      x: axis(candidate.x, element.clientWidth, width),
      y: axis(candidate.y, element.clientHeight, height),
    };
  };
  const update = (candidate: Viewport) => setViewport(constrain(candidate));
  const zoom = (delta: number) => update({ ...viewport, zoom: viewport.zoom + delta });
  const fit = () => {
    const element = viewportRef.current;
    const stage = stageRef.current;
    if (!element || !stage || !stage.offsetWidth || !stage.offsetHeight) return;
    update({
      zoom: clampZoom(Math.min(element.clientWidth / stage.offsetWidth, element.clientHeight / stage.offsetHeight, 1)),
      x: 0,
      y: 0,
    });
  };
  const localPoint = (event: ReactPointerEvent<HTMLDivElement>): Point => {
    const bounds = viewportRef.current?.getBoundingClientRect();
    return bounds
      ? { x: event.clientX - bounds.left, y: event.clientY - bounds.top }
      : { x: event.clientX, y: event.clientY };
  };
  const beginDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if ((event.target as Element).closest("button")) return;
    const point = localPoint(event);
    if (event.pointerType !== "touch") event.currentTarget.setPointerCapture(event.pointerId);
    dragRef.current = { pointerId: event.pointerId, point, view: viewport };
  };
  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const point = localPoint(event);
    update({ ...drag.view, x: drag.view.x + point.x - drag.point.x, y: drag.view.y + point.y - drag.point.y });
  };
  const endDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
    if (dragRef.current?.pointerId === event.pointerId) dragRef.current = null;
  };
  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const handled = () => { event.preventDefault(); event.stopPropagation(); };
    if (event.key === "ArrowLeft") { handled(); update({ ...viewport, x: viewport.x + PAN_STEP }); }
    else if (event.key === "ArrowRight") { handled(); update({ ...viewport, x: viewport.x - PAN_STEP }); }
    else if (event.key === "ArrowUp") { handled(); update({ ...viewport, y: viewport.y + PAN_STEP }); }
    else if (event.key === "ArrowDown") { handled(); update({ ...viewport, y: viewport.y - PAN_STEP }); }
  };

  const zoomPercent = Math.round(viewport.zoom * 100);
  return (
    <section className="tactical-board-panel" aria-labelledby="tactical-board-title">
      <header className="tactical-board-heading">
        <div><span className="eyebrow">Tactical board</span><h2 id="tactical-board-title">Encounter positions</h2></div>
        <p>{board.columns} by {board.rows} squares · {board.feetPerSquare} feet per square</p>
      </header>
      <div className="map-viewport-toolbar" role="toolbar" aria-label="Tactical board view controls">
        <button aria-label="Zoom tactical board out" disabled={viewport.zoom <= MIN_ZOOM} onClick={() => zoom(-ZOOM_STEP)} type="button"><Icon name="ZoomOut" size={17} /></button>
        <output aria-live="polite" aria-label="Current tactical board zoom">{zoomPercent}%</output>
        <button aria-label="Zoom tactical board in" disabled={viewport.zoom >= MAX_ZOOM} onClick={() => zoom(ZOOM_STEP)} type="button"><Icon name="ZoomIn" size={17} /></button>
        <button onClick={fit} type="button"><Icon name="Maximize2" size={16} /> Fit board</button>
        <button onClick={() => setViewport(DEFAULT_VIEW)} type="button"><Icon name="RotateCcw" size={16} /> Reset view</button>
      </div>
      <div
        aria-describedby="tactical-board-help"
        aria-keyshortcuts="ArrowLeft ArrowRight ArrowUp ArrowDown"
        aria-label={`Tactical encounter board. ${board.participants.length} visible combatants. ${board.turn ? `Current turn: ${board.turn.actorName}.` : "No active turn."}`}
        className="tactical-board-viewport"
        onKeyDown={handleKeyDown}
        onPointerCancel={endDrag}
        onPointerDown={beginDrag}
        onPointerMove={moveDrag}
        onPointerUp={endDrag}
        ref={viewportRef}
        tabIndex={0}
      >
        <div
          className="tactical-board-stage"
          ref={stageRef}
          style={{
            "--board-columns": board.columns,
            "--board-rows": board.rows,
            aspectRatio: `${board.columns} / ${board.rows}`,
            transform: `translate3d(${viewport.x}px, ${viewport.y}px, 0) scale(${viewport.zoom})`,
          } as CSSProperties}
        >
          {board.terrain.map((item) => <span aria-label={`${item.label} terrain. Movement cost ${item.movementCost}.`} className="tactical-board-terrain" key={item.id} role="img" style={placement(item.area, board)} />)}
          {board.obstacles.map((item) => <span aria-label={`${item.label}. Blocks movement.`} className="tactical-board-obstacle" key={item.id} role="img" style={placement(item.area, board)} />)}
          {board.participants.map((participant) => {
            const area = { x: participant.position.x, y: participant.position.y, width: participant.position.width, height: participant.position.height };
            return (
              <button
                aria-label={`${participant.name}. ${participant.active ? "Current turn. " : ""}Grid ${participant.position.x + 1}, ${participant.position.y + 1}. Footprint ${participant.position.width} by ${participant.position.height}. Elevation ${participant.position.elevationFeet} feet.`}
                aria-pressed={selectedId === participant.id}
                className={`tactical-board-token${participant.active ? " is-active" : ""}${selectedId === participant.id ? " is-selected" : ""}`}
                key={participant.id}
                onClick={() => setSelectedId(selectedId === participant.id ? "" : participant.id)}
                style={placement(area, board)}
                type="button"
              >
                <span aria-hidden="true">{participant.name.slice(0, 2).toUpperCase()}</span>
                <small aria-hidden="true">{participant.name}</small>
              </button>
            );
          })}
        </div>
      </div>
      <p className="world-map-panel__note" id="tactical-board-help">
        Drag the board or use arrow keys to pan. Only the visible buttons zoom, fit, or reset.
        Page scrolling never changes board zoom. Movement legality comes from the
        encounter mechanics, not this display.
      </p>
    </section>
  );
}
