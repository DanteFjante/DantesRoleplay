import { Icon } from "./Icon";

type DirectoryFilter = {
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
};

export function WorldDirectoryControls({
  query,
  searchLabel,
  placeholder,
  filters,
  onQueryChange,
}: {
  query: string;
  searchLabel: string;
  placeholder: string;
  filters: DirectoryFilter[];
  onQueryChange: (value: string) => void;
}) {
  return (
    <section className="world-directory-controls" aria-label={searchLabel}>
      <label className="world-directory-search">
        <span className="sr-only">{searchLabel}</span>
        <Icon name="Search" size={16} />
        <input
          maxLength={80}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder={placeholder}
          type="search"
          value={query}
        />
      </label>
      {filters.map((filter) => (
        <label key={filter.label}>
          <span>{filter.label}</span>
          <select onChange={(event) => filter.onChange(event.target.value)} value={filter.value}>
            {filter.options.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>
      ))}
    </section>
  );
}
