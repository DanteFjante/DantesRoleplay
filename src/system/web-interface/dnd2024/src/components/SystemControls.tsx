import { createElement } from "react";

/** Shared shell controls. Their preference and application discovery logic lives in BrowserComponents. */
export function SystemControls({ playerOnly = false }: { playerOnly?: boolean }) {
  return (
    <div className="system-controls" aria-label="Website navigation">
      {createElement("system-navigation", {
        "application-id": "dnd2024",
        "access-mode": playerOnly ? "player" : "operator",
      })}
    </div>
  );
}
