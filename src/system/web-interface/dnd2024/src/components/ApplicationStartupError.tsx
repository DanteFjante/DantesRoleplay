export function ApplicationStartupError({ kind, message, onRetry }: {
  kind: "denied" | "connection" | "unavailable";
  message: string;
  onRetry: () => void;
}) {
  const title = kind === "denied" ? "Website access denied"
    : kind === "connection" ? "Connection lost" : "Application unavailable";
  return <div className="information-hub">
    <header className="top-bar"><div className="brand-lockup">
      <span className="brand-lockup__die" aria-hidden="true">20</span>
      <span className="brand-lockup__copy"><strong>Dante&apos;s Roleplay</strong>
        <small>D&amp;D 2024 table</small></span>
    </div></header>
    <main id="information-content" className="information-content">
      <section className="view-unavailable" role="alert" data-view-status="error" data-reason-code={kind}>
        <h1 id="main-view-heading">{title}</h1>
        <p>{message}</p>
        {kind === "unavailable" && <p>The application could not start. This is a service problem, not a player restriction.</p>}
        <button className="subtle-button" type="button" onClick={onRetry}>Retry application</button>
      </section>
    </main>
  </div>;
}
