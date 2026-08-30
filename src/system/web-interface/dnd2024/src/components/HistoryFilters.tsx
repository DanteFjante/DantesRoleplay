import { Icon } from "./Icon";

export type HistoryOrder = "newest" | "oldest";

export function HistoryFilters({
  query,
  region,
  category,
  order,
  regions,
  categories,
  onQueryChange,
  onRegionChange,
  onCategoryChange,
  onOrderChange,
}: {
  query: string;
  region: string;
  category: string;
  order: HistoryOrder;
  regions: string[];
  categories: string[];
  onQueryChange: (value: string) => void;
  onRegionChange: (value: string) => void;
  onCategoryChange: (value: string) => void;
  onOrderChange: (value: HistoryOrder) => void;
}) {
  return (
    <section className="history-filters" aria-label="Filter world history">
      <label className="history-search">
        <span className="sr-only">Search world history</span>
        <Icon name="Search" size={16} />
        <input
          maxLength={80}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search events, people, or places"
          type="search"
          value={query}
        />
      </label>
      <label>
        <span>Region</span>
        <select onChange={(event) => onRegionChange(event.target.value)} value={region}>
          <option value="all">All regions</option>
          {regions.map((value) => <option key={value} value={value}>{value}</option>)}
        </select>
      </label>
      <label>
        <span>Category</span>
        <select onChange={(event) => onCategoryChange(event.target.value)} value={category}>
          <option value="all">All categories</option>
          {categories.map((value) => <option key={value} value={value}>{value}</option>)}
        </select>
      </label>
      <label>
        <span>Order</span>
        <select
          onChange={(event) => onOrderChange(event.target.value as HistoryOrder)}
          value={order}
        >
          <option value="newest">Newest first</option>
          <option value="oldest">Oldest first</option>
        </select>
      </label>
    </section>
  );
}
