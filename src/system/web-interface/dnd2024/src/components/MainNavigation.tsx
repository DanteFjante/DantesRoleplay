import type { MainTabId } from "../data/hub-types";
import { MAIN_TABS } from "../state.js";
import { Icon } from "./Icon";

export function MainNavigation({
  activeTab,
  availableTabs,
  chapter,
  onSelect,
}: {
  activeTab: MainTabId;
  availableTabs?: readonly MainTabId[];
  chapter: string;
  onSelect: (tab: MainTabId) => void;
}) {
  return (
    <aside className="main-nav-shell">
      <nav aria-label="Main table views" className="main-nav">
        {MAIN_TABS.map((tab) => {
          const tabId = tab.id as MainTabId;
          const available = availableTabs === undefined || availableTabs.includes(tabId);
          return (
            <button
              aria-current={activeTab === tabId ? "page" : undefined}
              className="main-nav__item"
              disabled={!available}
              key={tab.id}
              onClick={() => onSelect(tabId)}
              title={available ? undefined : "Requires an authorized private campaign"}
              type="button"
            >
              <Icon name={tab.icon} size={19} />
              <span>{tab.label}</span>
            </button>
          );
        })}
      </nav>
      <div className="main-nav__chapter">
        <span className="eyebrow">Current chapter</span>
        <strong>{chapter}</strong>
      </div>
    </aside>
  );
}
