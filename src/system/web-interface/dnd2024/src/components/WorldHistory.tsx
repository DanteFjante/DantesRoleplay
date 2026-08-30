"use client";

import { useMemo, useState } from "react";

import type { WorldReadModel } from "../data/hub-types";
import { filterWorldHistory } from "../state.js";
import { HistoryFilters, type HistoryOrder } from "./HistoryFilters";
import { HistoryTimeline } from "./HistoryTimeline";

export function WorldHistory({
  world,
  onOpenLocation,
}: {
  world: WorldReadModel;
  onOpenLocation: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("all");
  const [category, setCategory] = useState("all");
  const [order, setOrder] = useState<HistoryOrder>("newest");
  const regions = useMemo(
    () => [...new Set(world.history.map((event) => event.region))].sort(),
    [world.history],
  );
  const categories = useMemo(
    () => [...new Set(world.history.map((event) => event.category))].sort(),
    [world.history],
  );
  const events = useMemo(
    () => filterWorldHistory(world.history, { query, region, category, order }),
    [world.history, query, region, category, order],
  );

  return (
    <div className="world-history-view">
      <header className="atlas-heading history-heading">
        <div>
          <span className="eyebrow">A world that remembers</span>
          <h1 id="main-view-heading" tabIndex={-1}>{world.name} history</h1>
        </div>
        <p>
          {events.length} of {world.history.length} {world.history.length === 1 ? "event" : "events"}
        </p>
      </header>
      <p className="history-introduction">
        A record of turning points whose consequences still shape the world, independent of any
        single campaign.
      </p>
      <HistoryFilters
        categories={categories}
        category={category}
        onCategoryChange={setCategory}
        onOrderChange={setOrder}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        onRegionChange={setRegion}
        order={order}
        query={query}
        region={region}
        regions={regions}
      />
      <HistoryTimeline events={events} totalEvents={world.history.length} onOpenLocation={onOpenLocation} />
    </div>
  );
}
