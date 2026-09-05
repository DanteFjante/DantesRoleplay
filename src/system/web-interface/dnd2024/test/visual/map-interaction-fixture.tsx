import { useState } from "react";
import { createRoot } from "react-dom/client";
import { DEFAULT_MAP_VIEWPORT, MapCanvas } from "../../src/components/MapCanvas";
import { TacticalBoard } from "../../src/components/TacticalBoard";
import type { MapDocument, TacticalEncounterBoard } from "../../src/data/hub-types";
import "../../src/styles.css";

// Synthetic presentation data only. This fixture makes no game-server requests.
const map: MapDocument = {
  id: "map.test", scope: "region", parentMapId: null,
  subject: { kind: "region", id: "region.test", name: "Test Vale" },
  coordinateSpace: { id: "space.test", unit: "illustrative", width: 100, height: 100 },
  base: {
    imageUrl: "data:image/svg+xml," + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900"><rect width="1600" height="900" fill="#354d36"/></svg>'),
    alt: "Synthetic map background",
  },
  layers: [{ id: "layer.test", kind: "markers", order: 1, label: "Test places" }],
  features: [{
    id: "feature.keep", kind: "point", layerId: "layer.test", coordinateSpaceId: "space.test",
    geometry: { x: 50, y: 50 }, name: "Test Keep", detail: "Synthetic place for interaction tests.", locationId: "location.test",
  }],
  scopeLinks: [],
};
const board: TacticalEncounterBoard = {
  revision: 1, columns: 12, rows: 8, feetPerSquare: 5, terrain: [], obstacles: [],
  participants: [{
    id: "actor.test", name: "Test Hero", initiative: 10, active: true,
    position: { x: 4, y: 3, width: 1, height: 1, elevationFeet: 0, revision: 1 },
  }],
};

function Fixture() {
  const [viewport, setViewport] = useState(DEFAULT_MAP_VIEWPORT);
  const [selected, setSelected] = useState("feature.keep");
  const tactical = new URLSearchParams(location.search).get("surface") === "board";
  return <main style={{ width: "min(900px, calc(100% - 32px))", margin: "0 auto", paddingTop: 750, paddingBottom: 1600 }}>
    <h1>Isolated map interaction test</h1>
    <p>Maintained components and styles, synthetic data; not a live campaign.</p>
    {tactical ? <TacticalBoard board={board} /> : <>
      <MapCanvas
        map={map} viewport={viewport} onViewportChange={setViewport}
        selectedFeatureId={selected} onFeatureSelect={setSelected} currentLocationId=""
        annotatedFeatureIds={new Set()} influencedFeatureIds={new Set()} scopeLinkFeatureIds={new Map()}
        onOpenScope={() => {}}
      />
      <output data-testid="viewport-state">{JSON.stringify(viewport)}</output>
      <aside data-testid="selected-place">{selected ? "Test Keep selected" : "No place selected"}</aside>
    </>}
  </main>;
}

const root = document.getElementById("root");
if (!root) throw new Error("Missing test root");
createRoot(root).render(<Fixture />);
