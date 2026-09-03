import { MainNavigation } from "./MainNavigation";

export function BootstrapShell() {
  return (
    <div aria-busy="true" className="information-hub bootstrap-shell">
      <a className="skip-link" href="#information-content">Skip to information</a>
      <header className="bootstrap-shell__topbar">
        <strong>D&amp;D 2024</strong>
        <span>Preparing your authorized world view…</span>
      </header>
      <div className="information-hub__body">
        <MainNavigation
          activeTab="world"
          chapter="Campaign"
          progress="Loading current world"
          onSelect={() => {}}
        />
        <main className="information-content" id="information-content">
          <section className="bootstrap-shell__view" role="status">
            <span className="eyebrow">World</span>
            <h1 id="main-view-heading">Opening the world overview</h1>
            <p>The navigation is ready. Private campaign information will appear when its contract has been validated.</p>
          </section>
        </main>
      </div>
    </div>
  );
}
