import type { MainTabId } from "../data/hub-types";
import { MAIN_TABS } from "../state.js";
import { Icon } from "./Icon";

export function MainNavigation({
  activeTab,
  chapter,
  onSelect,
  progress,
}: {
  activeTab: MainTabId;
  chapter: string;
  onSelect: (tab: MainTabId) => void;
  progress: string;
}) {
  return (
    <aside className="main-nav-shell">
      <nav aria-label="Main table views" className="main-nav">
        {MAIN_TABS.map((tab) => (
          <button
            aria-current={activeTab === tab.id ? "page" : undefined}
            className="main-nav__item"
            key={tab.id}
            onClick={() => onSelect(tab.id as MainTabId)}
            type="button"
          >
            <Icon name={tab.icon} size={19} />
            <span>{tab.label}</span>
          </button>
        ))}
      </nav>
      <div className="main-nav__chapter">
        <span className="eyebrow">Current chapter</span>
        <strong>{chapter}</strong>
        <small>{progress}</small>
        <div className="chapter-progress" aria-label={progress}>
          <span style={{ width: "100%" }} />
        </div>
      </div>
    </aside>
  );
}
