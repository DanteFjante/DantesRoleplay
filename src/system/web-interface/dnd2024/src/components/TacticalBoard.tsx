import {
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  useRef,
  useState,
  useEffect,
  useId,
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

export function TacticalBoard({ board, placeholder = false }: { board: TacticalEncounterBoard; placeholder?: boolean }) {
  const gridId = useId();
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
  const focusParticipant = (id: string) => {
    const participant = board.participants.find((entry) => entry.id === id);
    const element = viewportRef.current;
    const stage = stageRef.current;
    if (!participant || !element || !stage) return;
    setSelectedId(id);
    update({
      zoom: viewport.zoom,
      x: element.clientWidth / 2 - (participant.position.x + participant.position.width / 2) / board.columns * stage.offsetWidth * viewport.zoom,
      y: element.clientHeight / 2 - (participant.position.y + participant.position.height / 2) / board.rows * stage.offsetHeight * viewport.zoom,
    });
  };
  useEffect(() => {
    setSelectedId("");
    setViewport(DEFAULT_VIEW);
    dragRef.current = null;
  }, [board]);
  useEffect(() => {
    const element = viewportRef.current;
    if (!element || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(() => setViewport((current) => constrain(current)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const localPoint = (event: ReactPointerEvent<HTMLDivElement>): Point => {
    const bounds = viewportRef.current?.getBoundingClientRect();
    return bounds
      ? { x: event.clientX - bounds.left, y: event.clientY - bounds.top }
      : { x: event.clientX, y: event.clientY };
  };
  const beginDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.pointerType === "touch" || event.button !== 0 || (event.target as Element).closest("button")) return;
    const point = localPoint(event);
    event.currentTarget.setPointerCapture(event.pointerId);
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
    if (event.target !== event.currentTarget) return;
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
        <p>{board.columns} by {board.rows} squares · {placeholder ? "Illustrative placeholder; no recorded scale" : `${board.feetPerSquare} feet per square`}</p>
      </header>
      <div className="map-viewport-toolbar" role="toolbar" aria-label="Tactical board view controls">
        <button aria-label="Zoom tactical board out" disabled={viewport.zoom <= MIN_ZOOM} onClick={() => zoom(-ZOOM_STEP)} type="button"><Icon name="ZoomOut" size={17} /></button>
        <output aria-live="polite" aria-label="Current tactical board zoom">{zoomPercent}%</output>
        <button aria-label="Zoom tactical board in" disabled={viewport.zoom >= MAX_ZOOM} onClick={() => zoom(ZOOM_STEP)} type="button"><Icon name="ZoomIn" size={17} /></button>
        <button onClick={fit} type="button"><Icon name="Maximize2" size={16} /> Fit board</button>
        <button onClick={() => update(DEFAULT_VIEW)} type="button"><Icon name="RotateCcw" size={16} /> Reset view</button>
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
          <svg className="tactical-board-svg" viewBox={`0 0 ${board.columns} ${board.rows}`} aria-label="Encounter grid" role="group">
            <defs><pattern id={gridId} width="1" height="1" patternUnits="userSpaceOnUse"><path d="M 1 0 L 0 0 0 1" fill="none" stroke="#748578" strokeWidth="0.025" /></pattern></defs>
            <g data-layer="background"><rect width={board.columns} height={board.rows} fill="#17211d" /></g>
            <g data-layer="terrain" aria-hidden="true">{board.terrain.map((item) => <rect key={item.id} {...item.area} fill="#344e32" stroke="#aac494" strokeWidth="0.03" strokeDasharray="0.1 0.07" />)}</g>
            <g data-layer="obstacles" aria-hidden="true">{board.obstacles.map((item) => <rect key={item.id} {...item.area} fill="#754d39" stroke="#d4a06f" strokeWidth="0.04" />)}</g>
            <g data-layer="grid" aria-hidden="true"><rect width={board.columns} height={board.rows} fill={`url(#${gridId})`} /></g>
            <g data-layer="tokens">{board.participants.map((participant) => (
              <g key={participant.id}>
              <rect aria-hidden="true" x={participant.position.x + 0.04} y={participant.position.y + 0.04} width={participant.position.width - 0.08} height={participant.position.height - 0.08} rx="0.45" fill={participant.active ? "#71532c" : "#20342c"} stroke={participant.active ? "#ffe1a6" : "#9aaea3"} strokeWidth="0.04" />
              <foreignObject x={participant.position.x} y={participant.position.y} width={participant.position.width} height={participant.position.height}>
              <button
                aria-label={`${participant.name}. ${participant.active ? "Current turn. " : ""}Grid ${participant.position.x + 1}, ${participant.position.y + 1}. Footprint ${participant.position.width} by ${participant.position.height}. Elevation ${participant.position.elevationFeet} feet.`}
                aria-pressed={selectedId === participant.id}
                className={`tactical-board-token${participant.active ? " is-active" : ""}${selectedId === participant.id ? " is-selected" : ""}`}
                key={participant.id}
                onClick={() => setSelectedId(selectedId === participant.id ? "" : participant.id)}
                onFocus={() => focusParticipant(participant.id)}
                type="button"
              >
                <span aria-hidden="true">{participant.name.slice(0, 2).toUpperCase()}</span>
                <small aria-hidden="true">{participant.name}</small>
              </button>
              </foreignObject>
              </g>
            ))}</g>
            <g data-layer="interaction" pointerEvents="none" aria-hidden="true">{board.participants.filter((participant) => participant.id === selectedId).map((participant) => <rect key={participant.id} x={participant.position.x} y={participant.position.y} width={participant.position.width} height={participant.position.height} fill="none" stroke="#ffe1a6" strokeWidth="0.06" />)}</g>
          </svg>
        </div>
      </div>
      <p className="world-map-panel__note" id="tactical-board-help">
        Drag the board or use arrow keys to pan. Only the visible buttons zoom, fit, or reset.
        Page scrolling never changes board zoom. Movement legality comes from the
        encounter mechanics, not this display.
      </p>
      <section className="tactical-board-description" aria-label="Board text alternative">
        <h3>Combatants and coordinates</h3>
        <p aria-live="polite">{board.turn ? `Current turn: ${board.turn.actorName}.` : "No visible active turn on this board."}</p>
        <p>Coordinates start at column 1, row 1 in the top-left corner. Focus pans to a token without changing zoom or moving it.</p>
        {board.participants.length ? <ol>{board.participants.map((participant) => <li key={participant.id}>
          <button type="button" onClick={() => focusParticipant(participant.id)} aria-pressed={selectedId === participant.id}>Focus {participant.name}</button>
          <span>Initiative {participant.initiative}. Column {participant.position.x + 1}, row {participant.position.y + 1}. Footprint {participant.position.width} × {participant.position.height} squares. Elevation {participant.position.elevationFeet} feet.{participant.active ? " Current turn." : ""}</span>
        </li>)}</ol> : <p>No visible recorded token positions. The Initiative list remains available below.</p>}
        <h3>Obstacle legend</h3>
        <p>Brown areas are accepted movement-blocking obstacles; green areas are accepted terrain.</p>
        {board.obstacles.length ? <ul>{board.obstacles.map((item) => <li key={item.id}><strong>{item.label}</strong><span>Column {item.area.x + 1}, row {item.area.y + 1}; {item.area.width} × {item.area.height} squares. Blocks movement.</span></li>)}</ul> : <p>No visible recorded obstacles.</p>}
        {board.terrain.length ? <ul>{board.terrain.map((item) => <li key={item.id}><strong>{item.label}</strong><span>Column {item.area.x + 1}, row {item.area.y + 1}; {item.area.width} × {item.area.height} squares. Movement cost {item.movementCost}.</span></li>)}</ul> : <p>No visible recorded terrain.</p>}
      </section>
    </section>
  );
}
