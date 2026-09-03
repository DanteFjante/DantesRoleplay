import { Component, type ErrorInfo, type ReactNode } from "react";

export class ViewErrorBoundary extends Component<{
  children: ReactNode;
  viewLabel: string;
}, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(_error: Error, _info: ErrorInfo) {
    // Rendering failures stay local to this view. The browser console retains React's diagnostic.
  }

  render() {
    if (this.state.failed) {
      return (
        <section className="view-render-error" role="alert">
          <span className="eyebrow">View unavailable</span>
          <h1 id="main-view-heading" tabIndex={-1}>{this.props.viewLabel} could not be displayed</h1>
          <p>The rest of the table is still available. Retry this view or open another section.</p>
          <small>Diagnostic: view-render:{this.props.viewLabel}</small>
          <button onClick={() => this.setState({ failed: false })} type="button">Retry view</button>
        </section>
      );
    }
    return this.props.children;
  }
}
