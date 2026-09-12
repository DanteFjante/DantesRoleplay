import { useRef, type KeyboardEvent } from "react";
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
  const buttons = useRef<Array<HTMLButtonElement | null>>([]);
  const tabs = MAIN_TABS.filter((tab) => availableTabs === undefined || availableTabs.includes(tab.id as MainTabId));

  function moveFocus(event: KeyboardEvent<HTMLElement>, current: MainTabId) {
    const index = tabs.findIndex((tab) => tab.id === current);
    const direction = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1
      : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
    const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1
      : direction ? (index + direction + tabs.length) % tabs.length : -1;
    if (next < 0) return;
    event.preventDefault();
    const target = MAIN_TABS.findIndex((tab) => tab.id === tabs[next]?.id);
    buttons.current[target]?.focus();
  }

  return (
    <aside className="main-nav-shell">
      <label className="main-nav__mobile-label">
        <span>D&amp;D table view</span>
        <select aria-label="D&D table view" value={activeTab}
          onChange={(event) => onSelect(event.currentTarget.value as MainTabId)}>
          {MAIN_TABS.map((tab) => <option key={tab.id} value={tab.id}
            disabled={availableTabs !== undefined && !availableTabs.includes(tab.id as MainTabId)}>{tab.label}</option>)}
        </select>
      </label>
      <nav aria-label="Main table views" data-navigation-scope="dnd-local" className="main-nav">
        {MAIN_TABS.map((tab) => {
          const tabId = tab.id as MainTabId;
          const available = availableTabs === undefined || availableTabs.includes(tabId);
          return (
            <button
              aria-current={activeTab === tabId ? "page" : undefined}
              className="main-nav__item"
              disabled={!available}
              key={tab.id}
              onKeyDown={(event) => moveFocus(event, tabId)}
              onClick={() => onSelect(tabId)}
              ref={(element) => { buttons.current[MAIN_TABS.indexOf(tab)] = element; }}
              tabIndex={activeTab === tabId ? 0 : -1}
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
