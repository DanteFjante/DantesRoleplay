import {
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  useEffect,
  useRef,
  useState,
} from "react";
import type { MapDocument, MapFeature } from "../data/hub-types";
import { Icon } from "./Icon";
import { markMapReady } from "../observability/performance.js";

export type MapViewportState = { zoom: number; x: number; y: number };

export const DEFAULT_MAP_VIEWPORT: MapViewportState = { zoom: 1, x: 0, y: 0 };
const MIN_ZOOM = 0.5;
const MAX_ZOOM = 4;
const ZOOM_STEP = 0.25;
const KEYBOARD_PAN_STEP = 64;

type Point = { x: number; y: number };
type DragGesture = { kind: "drag"; pointerId: number; point: Point; view: MapViewportState };

/** Every marker stays in the coordinate space declared by its current map. */
function placement(feature: MapFeature, map: MapDocument) {
  return {
    left: `${(feature.geometry.x / map.coordinateSpace.width) * 100}%`,
    top: `${(feature.geometry.y / map.coordinateSpace.height) * 100}%`,
  };
}

function clampZoom(value: number) {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
}

export function MapCanvas({
  map,
  selectedFeatureId,
  currentLocationId,
  scopeLinkFeatureIds,
  annotatedFeatureIds,
  influencedFeatureIds,
  viewport,
  onViewportChange,
  onFeatureSelect,
  onOpenScope,
}: {
  map: MapDocument;
  selectedFeatureId: string;
  currentLocationId: string;
  scopeLinkFeatureIds: Map<string, string>;
  annotatedFeatureIds: Set<string>;
  influencedFeatureIds: Set<string>;
  viewport: MapViewportState;
  onViewportChange: (viewport: MapViewportState) => void;
  onFeatureSelect: (featureId: string) => void;
  onOpenScope: (mapId: string) => void;
}) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const gestureRef = useRef<DragGesture | null>(null);
  const movedRef = useRef(false);
  const [failedImageUrl, setFailedImageUrl] = useState<string | null>(null);
  const [imageAttempt, setImageAttempt] = useState(0);
  const imageFailed = !!map.base && failedImageUrl === map.base.imageUrl;

  useEffect(() => {
    if (!map.base) markMapReady(map.id);
  }, [map.id, map.base]);

  const constrain = (candidate: MapViewportState): MapViewportState => {
    const viewportElement = viewportRef.current;
    const stage = stageRef.current;
    const zoom = clampZoom(candidate.zoom);
    if (!viewportElement || !stage) return { zoom, x: candidate.x, y: candidate.y };
    const width = stage.offsetWidth * zoom;
    const height = stage.offsetHeight * zoom;
    const axis = (value: number, viewportSize: number, contentSize: number) =>
      contentSize <= viewportSize
        ? (viewportSize - contentSize) / 2
        : Math.min(0, Math.max(viewportSize - contentSize, value));
    return {
      zoom,
      x: axis(candidate.x, viewportElement.clientWidth, width),
      y: axis(candidate.y, viewportElement.clientHeight, height),
    };
  };

  const updateViewport = (candidate: MapViewportState) => onViewportChange(constrain(candidate));

  const localPoint = (clientX: number, clientY: number): Point => {
    const bounds = viewportRef.current?.getBoundingClientRect();
    return bounds
      ? { x: clientX - bounds.left, y: clientY - bounds.top }
      : { x: clientX, y: clientY };
  };

  const zoomAt = (nextZoom: number, focal?: Point) => {
    const element = viewportRef.current;
    if (!element) return;
    const point = focal ?? { x: element.clientWidth / 2, y: element.clientHeight / 2 };
    const zoom = clampZoom(nextZoom);
    const contentX = (point.x - viewport.x) / viewport.zoom;
    const contentY = (point.y - viewport.y) / viewport.zoom;
    updateViewport({ zoom, x: point.x - contentX * zoom, y: point.y - contentY * zoom });
  };

  const fitMap = () => {
    const element = viewportRef.current;
    const stage = stageRef.current;
    if (!element || !stage || stage.offsetWidth === 0 || stage.offsetHeight === 0) return;
    const zoom = clampZoom(Math.min(
      element.clientWidth / stage.offsetWidth,
      element.clientHeight / stage.offsetHeight,
      1,
    ));
    updateViewport({ zoom, x: 0, y: 0 });
  };

  const resetView = () => updateViewport(DEFAULT_MAP_VIEWPORT);

  const focusSelected = () => {
    const feature = map.features.find((candidate) => candidate.id === selectedFeatureId);
    const element = viewportRef.current;
    const stage = stageRef.current;
    if (!feature || !element || !stage) return;
    const zoom = Math.max(2, viewport.zoom);
    const x = (feature.geometry.x / map.coordinateSpace.width) * stage.offsetWidth;
    const y = (feature.geometry.y / map.coordinateSpace.height) * stage.offsetHeight;
    updateViewport({
      zoom,
      x: element.clientWidth / 2 - x * zoom,
      y: element.clientHeight / 2 - y * zoom,
    });
    viewportRef.current?.focus({ preventScroll: true });
  };

  const beginGesture = (event: ReactPointerEvent<HTMLDivElement>) => {
    if ((event.target as Element).closest("button")) return;
    const activeGesture = gestureRef.current;
    if (activeGesture) {
      if (event.currentTarget.hasPointerCapture(activeGesture.pointerId)) {
        event.currentTarget.releasePointerCapture(activeGesture.pointerId);
      }
      gestureRef.current = null;
      movedRef.current = false;
      return;
    }
    const point = localPoint(event.clientX, event.clientY);
    if (event.pointerType !== "touch") event.currentTarget.setPointerCapture(event.pointerId);
    movedRef.current = false;
    gestureRef.current = { kind: "drag", pointerId: event.pointerId, point, view: viewport };
  };

  const moveGesture = (event: ReactPointerEvent<HTMLDivElement>) => {
    const gesture = gestureRef.current;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const point = localPoint(event.clientX, event.clientY);
    const dx = point.x - gesture.point.x;
    const dy = point.y - gesture.point.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) movedRef.current = true;
    updateViewport({ ...gesture.view, x: gesture.view.x + dx, y: gesture.view.y + dy });
  };

  const endGesture = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    if (gestureRef.current?.pointerId === event.pointerId) gestureRef.current = null;
  };

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const handled = () => {
      event.preventDefault();
      event.stopPropagation();
    };
    if (event.key === "ArrowLeft") {
      handled(); updateViewport({ ...viewport, x: viewport.x + KEYBOARD_PAN_STEP });
    } else if (event.key === "ArrowRight") {
      handled(); updateViewport({ ...viewport, x: viewport.x - KEYBOARD_PAN_STEP });
    } else if (event.key === "ArrowUp") {
      handled(); updateViewport({ ...viewport, y: viewport.y + KEYBOARD_PAN_STEP });
    } else if (event.key === "ArrowDown") {
      handled(); updateViewport({ ...viewport, y: viewport.y - KEYBOARD_PAN_STEP });
    }
  };

  const zoomPercent = Math.round(viewport.zoom * 100);
  return (
    <section className="world-map-panel" aria-label={`${map.subject.name} map`}>
      <div className="map-viewport-toolbar" role="toolbar" aria-label="Map view controls">
        <button aria-label="Zoom out" disabled={viewport.zoom <= MIN_ZOOM} onClick={() => zoomAt(viewport.zoom - ZOOM_STEP)} type="button">
          <Icon name="ZoomOut" size={17} />
        </button>
        <output aria-live="polite" aria-label="Current map zoom">{zoomPercent}%</output>
        <button aria-label="Zoom in" disabled={viewport.zoom >= MAX_ZOOM} onClick={() => zoomAt(viewport.zoom + ZOOM_STEP)} type="button">
          <Icon name="ZoomIn" size={17} />
        </button>
        <button onClick={fitMap} type="button"><Icon name="Maximize2" size={16} /> Fit map</button>
        <button onClick={resetView} type="button"><Icon name="RotateCcw" size={16} /> Reset view</button>
        <button disabled={!selectedFeatureId} onClick={focusSelected} type="button">
          <Icon name="Focus" size={16} /> Focus selected
        </button>
      </div>
      <div
        aria-describedby="map-viewport-help"
        aria-keyshortcuts="ArrowLeft ArrowRight ArrowUp ArrowDown"
        aria-label={`${map.subject.name} interactive map`}
        className="world-map-canvas"
        data-base={map.base && !imageFailed ? "present" : "absent"}
        onClick={() => {
          if (movedRef.current) { movedRef.current = false; return; }
          onFeatureSelect("");
        }}
        onKeyDown={handleKeyDown}
        onPointerCancel={endGesture}
        onPointerDown={beginGesture}
        onPointerMove={moveGesture}
        onPointerUp={endGesture}
        ref={viewportRef}
        tabIndex={0}
      >
        <div
          className="world-map-stage"
          ref={stageRef}
          style={{ transform: `translate3d(${viewport.x}px, ${viewport.y}px, 0) scale(${viewport.zoom})` }}
        >
          {map.base && !imageFailed ? (
            <img alt={map.base.alt} draggable={false} src={map.base.imageUrl}
              key={`${map.base.imageUrl}:${imageAttempt}`}
              onLoad={() => markMapReady(map.id)}
              onError={() => setFailedImageUrl(map.base!.imageUrl)} />
          ) : imageFailed ? (
            <div className="map-base-absent" role="alert">
              <p>The map image could not be loaded. The places below are still available.</p>
              <button type="button" onClick={(event) => {
                event.stopPropagation();
                setFailedImageUrl(null);
                setImageAttempt((attempt) => attempt + 1);
              }}>Try loading the map again</button>
            </div>
          ) : (
            <p className="map-base-absent">
              <Icon name="Map" size={20} />
              Map not available for {map.subject.name}. The places below remain readable as information.
            </p>
          )}
          <div className="world-map-markers" aria-label={`${map.subject.name} places`}>
            {map.features.map((feature) => {
              const childMapId = scopeLinkFeatureIds.get(feature.id);
              const isCurrent = feature.locationId !== null && feature.locationId === currentLocationId;
              const markerStyle = {
                ...placement(feature, map),
                "--map-marker-scale": `${1 / viewport.zoom}`,
              } as CSSProperties;
              return (
                <button
                  aria-label={`${feature.name}. ${feature.detail}${isCurrent ? " Current location." : ""}${
                    childMapId ? " Opens a closer map." : ""
                  }`}
                  aria-pressed={feature.id === selectedFeatureId}
                  className="world-map-marker"
                  data-annotated={annotatedFeatureIds.has(feature.id) ? "true" : undefined}
                  data-current={isCurrent ? "true" : undefined}
                  data-feature-id={feature.id}
                  data-influenced={influencedFeatureIds.has(feature.id) ? "true" : undefined}
                  data-scope-link={childMapId ? "true" : undefined}
                  key={feature.id}
                  onClick={(event) => {
                    event.stopPropagation();
                    onFeatureSelect(feature.id);
                  }}
                  onDoubleClick={childMapId ? (event) => {
                    event.stopPropagation();
                    onOpenScope(childMapId);
                  } : undefined}
                  style={markerStyle}
                  type="button"
                >
                  <span className="world-map-marker__pin" aria-hidden="true">
                    <Icon name={isCurrent ? "LocateFixed" : childMapId ? "Landmark" : "MapPin"} size={15} />
                  </span>
                  <span aria-hidden="true" className="world-map-marker__label">{feature.name}</span>
                  <span aria-hidden="true" className="world-map-marker__preview">
                    {feature.preview ? <img alt="" draggable={false} src={feature.preview.imageUrl} /> : null}
                    <span><strong>{feature.name}</strong><small>{feature.detail}</small></span>
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
      <p className="world-map-panel__note" id="map-viewport-help">
        Drag the map or use arrow keys to pan. Only the visible buttons zoom, fit, reset, or focus
        the selected place. Page scrolling and browser gestures never change map zoom. Placement is
        illustrative within this scope only and calculates no travel.
      </p>
    </section>
  );
}
