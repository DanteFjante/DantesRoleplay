import { MainNavigation } from "./MainNavigation";
import { useEffect } from "react";
import { markShellReady } from "../observability/performance.js";

export function BootstrapShell() {
  useEffect(() => { markShellReady(); }, []);
  return (
    <div aria-busy="true" className="information-hub bootstrap-shell">
      <a className="skip-link" href="#information-content">Skip to information</a>
      <header className="bootstrap-shell__topbar">
        <strong>D&amp;D 2024</strong>
        <span>Opening the shared table…</span>
      </header>
      <div className="information-hub__body">
        <MainNavigation
          activeTab="world"
          chapter="Campaign"
          onSelect={() => {}}
        />
        <main className="information-content" id="information-content">
          <section className="bootstrap-shell__view" role="status">
            <span className="eyebrow">World</span>
            <h1 id="main-view-heading">Opening the world overview</h1>
            <p>Loading the selected world and campaign from the game server.</p>
          </section>
        </main>
      </div>
    </div>
  );
}
