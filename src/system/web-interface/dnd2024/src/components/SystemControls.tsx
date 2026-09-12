import { createElement } from "react";

/** Shared shell controls. Their preference and application discovery logic lives in BrowserComponents. */
export function SystemControls() {
  return (
    <div className="system-controls" aria-label="Website controls">
      {createElement("system-navigation", { "application-id": "dnd2024" })}
      {createElement("system-theme-toggle", { "aria-label": "Color theme" })}
    </div>
  );
}
