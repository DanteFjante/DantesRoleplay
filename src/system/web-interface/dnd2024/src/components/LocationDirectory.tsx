import { useState } from "react";
import type { WorldLocation } from "../data/hub-types";
import { Icon } from "./Icon";
import { WorldDirectoryControls } from "./WorldDirectoryControls";
import "./LocationDirectory.css";

const PAGE_SIZE = 25;

export function LocationDirectory({ locations, currentLocationId, selectedLocationId, onSelect }: {
  locations: WorldLocation[];
  currentLocationId: string;
  selectedLocationId: string;
  onSelect: (locationId: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("");
  const [kind, setKind] = useState("");
  const [page, setPage] = useState(0);
  const needle = query.trim().toLocaleLowerCase();
  const matching = locations.filter((location) => (!region || location.region === region) &&
    (!kind || location.kind === kind) && (!needle ||
      [location.name, location.region, location.kind, location.summary].some((text) => text.toLocaleLowerCase().includes(needle))))
    .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
  const pageCount = Math.max(1, Math.ceil(matching.length / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);
  const start = currentPage * PAGE_SIZE;
  const shown = matching.slice(start, start + PAGE_SIZE);
  const options = (values: string[]) => [...new Set(values)].sort().map((value) => ({ value, label: value }));

  return <section className="location-browser location-directory" aria-labelledby="location-directory-heading">
    <div className="location-browser__heading">
      <div><span className="eyebrow">Across the world</span><h2 id="location-directory-heading">All locations</h2></div>
      <span>{locations.length}</span>
    </div>
    <WorldDirectoryControls query={query} searchLabel="Search all locations" placeholder="Search all locations…"
      onQueryChange={(value) => { setQuery(value); setPage(0); }} filters={[
        { label: "Region", value: region, options: [{ value: "", label: "All regions" }, ...options(locations.map((location) => location.region))],
          onChange: (value) => { setRegion(value); setPage(0); } },
        { label: "Type", value: kind, options: [{ value: "", label: "All types" }, ...options(locations.map((location) => location.kind))],
          onChange: (value) => { setKind(value); setPage(0); } },
      ]} />
    <div className="location-list" aria-label="All world locations">
      {shown.map((location) => <div className="location-row-group" data-record-id={location.id} key={location.id}>
        <button className="location-row" aria-label={`View details for ${location.name}`} aria-pressed={selectedLocationId === location.id}
          onClick={() => onSelect(location.id)} type="button">
          <span className="location-row__mark"><Icon name="MapPin" size={17} /></span>
          <span className="location-row__copy"><strong>{location.name}</strong><small>{location.region} · {location.kind}</small></span>
          <span className="location-row__action">{location.id === currentLocationId ? <em>Current</em> : null}<small>Details</small><Icon name="ChevronRight" size={16} /></span>
        </button>
      </div>)}
      {!shown.length ? <div className="location-empty"><Icon name="Search" /><strong>{locations.length ? "No matching locations" : "No locations available"}</strong>
        <p>{locations.length ? "Try a different name, region, or type." : "No locations are listed in this directory."}</p></div> : null}
    </div>
    <nav className="location-directory__pagination" aria-label="Location pages">
      <button disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)} type="button">Previous</button>
      <span role="status">{matching.length ? `${start + 1}–${start + shown.length}` : "0"} of {matching.length}</span>
      <button disabled={currentPage + 1 >= pageCount} onClick={() => setPage(currentPage + 1)} type="button">Next</button>
    </nav>
  </section>;
}
