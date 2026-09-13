"use client";

import { useEffect, useMemo, useState } from "react";

import type { WorldReadModel } from "../data/hub-types";
import { filterWorldLore } from "../state.js";
import { selectWorldLore } from "../data/presentation-records";
import { Icon } from "./Icon";
import { LoreCard } from "./LoreCard";
import { PagedWorldLore } from "./PagedWorldLore";
import type { LorePageLoader } from "../data/world-lore-page";
import { WorldDirectoryControls } from "./WorldDirectoryControls";

function StoredWorldLore({
  world,
  loading = false,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  world: WorldReadModel;
  loading?: boolean;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState("all");
  const [status, setStatus] = useState("all");
  const selected = useMemo(() => selectWorldLore((world as { lore?: unknown }).lore), [world.lore]);
  useEffect(() => { setQuery(""); setCategory("all"); setStatus("all"); }, [world.id]);
  const categories = useMemo(
    () => [...new Set(selected.records.map((entry) => entry.category))].sort(),
    [selected.records],
  );
  const statuses = useMemo(
    () => [...new Set(selected.records.map((entry) => entry.status))].sort(),
    [selected.records],
  );
  const entries = useMemo(
    () => filterWorldLore(selected.records, { query, category, status }),
    [selected.records, query, category, status],
  );
  const partial = world.loreCoverage === "partial" || selected.omittedCount > 0;

  return (
    <div className="world-directory-view" aria-busy={loading}>
      <header className="atlas-heading">
        <div>
          <span className="eyebrow">An encyclopedia of {world.name}</span>
          <h1 id="main-view-heading" tabIndex={-1}>Lore</h1>
        </div>
        <p>{loading
          ? `${entries.length} shown · ${selected.records.length} loaded so far`
          : `${entries.length} of ${selected.records.length} visible`}</p>
      </header>
      <p className="world-directory-introduction">
        Customs, relics, places, rumours, and established truths available in this perspective.
      </p>
      {loading ? <p role="status" className="directory-completeness-notice">
        Loading more lore. The complete count will appear when loading finishes.
      </p> : partial ? <p role="status" className="directory-completeness-notice">
        Some lore fields or records are unavailable. The readable entries are shown below.
      </p> : null}
      <WorldDirectoryControls
        filters={[
          {
            label: "Category",
            value: category,
            onChange: setCategory,
            options: [
              { value: "all", label: "All categories" },
              ...categories.map((value) => ({ value, label: value })),
            ],
          },
          {
            label: "Status",
            value: status,
            onChange: setStatus,
            options: [
              { value: "all", label: "All statuses" },
              ...statuses.map((value) => ({ value, label: value })),
            ],
          },
        ]}
        onQueryChange={(value) => setQuery(value.slice(0, 80))}
        placeholder="Search customs, relics, places, or people"
        query={query}
        searchLabel="Search world lore"
      />
      {entries.length ? (
        <div className="lore-grid">
          {entries.map((entry) => (
            <LoreCard
              entry={entry}
              key={entry.id}
              onOpenFaction={onOpenFaction}
              onOpenHistory={onOpenHistory}
              onOpenLocation={onOpenLocation}
            />
          ))}
        </div>
      ) : (
        <div className="directory-empty">
          <Icon name="BookOpen" size={26} />
          <strong>{loading && selected.records.length === 0 ? "Loading lore"
            : partial && selected.records.length === 0 ? "Lore unavailable" : "No lore matches"}</strong>
          <p>{loading && selected.records.length === 0 ? "Readable entries will appear as they arrive."
            : partial && selected.records.length === 0
              ? "This response does not establish an empty lore collection." : "Try another phrase, category, or status."}</p>
        </div>
      )}
    </div>
  );
}

export function WorldLore(props: Parameters<typeof StoredWorldLore>[0] & { loadPage?: LorePageLoader; pageScope?: string }) {
  return props.loadPage ? <PagedWorldLore worldName={props.world.name} scopeKey={props.pageScope ?? props.world.id}
    loadPage={props.loadPage} onOpenLocation={props.onOpenLocation} onOpenFaction={props.onOpenFaction} onOpenHistory={props.onOpenHistory} />
    : <StoredWorldLore {...props} />;
}
