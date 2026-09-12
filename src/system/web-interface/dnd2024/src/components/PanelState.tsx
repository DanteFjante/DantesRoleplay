import { Component, type ErrorInfo, type ReactNode } from "react";

export function PanelState({
  state,
  title,
  message,
  onRetry,
  retryLabel = "Retry",
}: {
  state: "loading" | "empty" | "error";
  title: string;
  message: string;
  onRetry?: () => void;
  retryLabel?: string;
}) {
  return (
    <section
      aria-busy={state === "loading" ? "true" : undefined}
      className={`panel-state panel-state--${state}`}
      role={state === "error" ? "alert" : "status"}
    >
      <span className="eyebrow">{state === "loading" ? "Loading" : state === "error" ? "Unavailable" : "No records"}</span>
      <h1 id="main-view-heading" tabIndex={-1}>{title}</h1>
      <p>{message}</p>
      {state === "error" && onRetry ? <button onClick={onRetry} type="button">{retryLabel}</button> : null}
    </section>
  );
}

export class PanelErrorBoundary extends Component<{
  children: ReactNode;
  label: string;
  resetKey: string;
}, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(_error: Error, _info: ErrorInfo) {
    // Rendering failures stay inside the consuming panel. React retains the diagnostic.
  }

  componentDidUpdate(previous: Readonly<{ children: ReactNode; label: string; resetKey: string }>) {
    if (this.state.failed && previous.resetKey !== this.props.resetKey) this.setState({ failed: false });
  }

  render() {
    if (this.state.failed) {
      return <PanelState state="error" title={`${this.props.label} could not be displayed`}
        message="Other table information remains available. Open another view or retry this panel."
        onRetry={() => this.setState({ failed: false })} />;
    }
    return this.props.children;
  }
}
