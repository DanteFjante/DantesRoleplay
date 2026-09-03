import type { PartyDossierEntry, SectionState } from "../../data/hub-types";
import { Icon } from "../Icon";

export function CharacterLoadingSkeleton({ label }: { label: string }) {
  return (
    <div aria-label={`Loading ${label}`} className="character-skeleton" role="status">
      <span className="sr-only">Refreshing {label}; the current view remains available.</span>
      <div className="character-skeleton__wide" />
      <div className="character-skeleton__grid">
        {Array.from({ length: 6 }, (_, index) => <i key={index} />)}
      </div>
    </div>
  );
}

export function CharacterSectionState({
  label,
  loading,
  onRetry,
  state,
}: {
  label: string;
  loading: boolean;
  onRetry?: () => void;
  state: SectionState<PartyDossierEntry[]>;
}) {
  if (loading) return <CharacterLoadingSkeleton label={label} />;
  if (state.status === "stale") {
    return (
      <div className="character-state character-state--stale" role="status">
        <Icon name="Clock3" size={18} />
        <div>
          <strong>Last confirmed {label}</strong>
          <p>The refresh failed, so the existing canonical information remains visible.</p>
          <small>Diagnostic: {state.diagnosticId}</small>
        </div>
        {onRetry ? <button onClick={onRetry} type="button">Retry {label}</button> : null}
      </div>
    );
  }
  if (state.status === "forbidden" || state.status === "error") {
    const incompatible = state.status === "error" && state.failureCategory === "incompatible-data";
    return (
      <div className="character-state character-state--error" role="alert">
        <Icon name="Shield" size={18} />
        <div>
          <strong>{state.status === "forbidden" ? "Information restricted" : "Character data unavailable"}</strong>
          <p>{state.status === "forbidden"
            ? `This seat is not authorized to read ${label}.`
            : incompatible
              ? `The server returned incompatible ${label}; no values were displayed.`
              : `${label} could not be loaded; no provisional values were substituted.`}</p>
          <small>Diagnostic: {state.diagnosticId}</small>
        </div>
        {onRetry ? <button onClick={onRetry} type="button">Retry {label}</button> : null}
      </div>
    );
  }
  return null;
}
