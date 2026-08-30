import type { Perspective } from "../data/hub-types";

export function PerspectiveSwitch({
  perspective,
  allowedPerspectives,
  busy,
  onChange,
}: {
  perspective: Perspective;
  allowedPerspectives: Perspective[];
  busy: boolean;
  onChange: (perspective: Perspective) => void;
}) {
  const dmAllowed = allowedPerspectives.includes("dm");
  const playerAllowed = allowedPerspectives.includes("player");

  return (
    <div className="perspective-switch" aria-label="Table perspective" role="group">
      <span className="perspective-switch__label">View as</span>
      <div className="perspective-switch__options">
        <button
          aria-pressed={perspective === "dm"}
          disabled={busy || !dmAllowed}
          onClick={() => onChange("dm")}
          title={dmAllowed ? "Use the DM perspective" : "DM access is not available for this seat"}
          type="button"
        >
          DM
        </button>
        <button
          aria-pressed={perspective === "player"}
          disabled={busy || !playerAllowed}
          onClick={() => onChange("player")}
          title={playerAllowed ? "Use the Player perspective" : "Player access is not available for this seat"}
          type="button"
        >
          Player
        </button>
      </div>
      <span className="sr-only" role="status">{busy ? "Changing perspective" : `${perspective} perspective active`}</span>
    </div>
  );
}
