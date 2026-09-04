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
  if (state.status === "empty") {
    return (
      <div className="character-state character-state--empty" role="status">
        <Icon name="BookOpen" size={18} />
        <div>
          <strong>No {label} recorded</strong>
          <p>No canonical {label} is recorded for this character yet.</p>
        </div>
        {onRetry ? <button onClick={onRetry} type="button">Check again</button> : null}
      </div>
    );
  }
  if (state.status === "stale") {
    return (
      <div className="character-state character-state--stale" role="status">
        <Icon name="Clock3" size={18} />
        <div>
          <strong>Last confirmed {label}</strong>
          <p>{state.failureCategory === "stale-data"
            ? `The source revision changed, so the last confirmed ${label} remains visible while you reload.`
            : "The refresh failed, so the existing canonical information remains visible."}</p>
          {state.errorCode ? <small>Code: {state.errorCode}</small> : null}
          <small>Diagnostic: {state.diagnosticId}</small>
        </div>
        {onRetry ? <button onClick={onRetry} type="button">Retry {label}</button> : null}
      </div>
    );
  }
  if (state.status === "forbidden" || state.status === "error") {
    const incompatible = state.status === "error" && state.failureCategory === "incompatible-data";
    const stale = state.status === "error" && state.failureCategory === "stale-data";
    const transport = state.status === "error" && state.failureCategory === "transport";
    const serviceUnavailable = state.status === "error" && state.failureCategory === "http" &&
      state.httpStatus !== undefined && state.httpStatus >= 500;
    const title = state.status === "forbidden"
      ? "Information restricted"
      : incompatible
        ? "Character data incompatible"
        : stale
          ? "Character data changed"
          : transport
            ? "Connection unavailable"
            : serviceUnavailable
              ? "Character service unavailable"
              : "Character data unavailable";
    const message = state.status === "forbidden"
      ? `This seat is not authorized to read ${label}. Ask the DM to check the current seat and campaign binding.`
      : incompatible
        ? `The server returned incompatible ${label}; no values were displayed. Reload after the page and catalog revisions are aligned.`
        : stale
          ? `The ${label} fingerprint no longer matches the active application. Reload the current application data.`
          : transport
            ? `The server could not be reached for ${label}. Check the connection and try again.`
            : serviceUnavailable
              ? `${label} is temporarily unavailable. Try again after the service recovers.`
              : `${label} could not be loaded; no provisional values were substituted. Check the diagnostic and try again.`;
    return (
      <div className="character-state character-state--error" role="alert">
        <Icon name="Shield" size={18} />
        <div>
          <strong>{title}</strong>
          <p>{message}</p>
          {state.errorCode ? <small>Code: {state.errorCode}</small> : null}
          <small>Diagnostic: {state.diagnosticId}</small>
        </div>
        {onRetry ? <button onClick={onRetry} type="button">Retry {label}</button> : null}
      </div>
    );
  }
  return null;
}
