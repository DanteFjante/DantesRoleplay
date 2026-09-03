import type { RuleReadModel } from "../data/hub-types";
import { MainNavigation } from "./MainNavigation";
import { RulesView } from "./RulesView";
import { InstalledContentView } from "./InstalledContentView";
import type { InstalledContentModel } from "../server/effective-content";
import { useEffect, useState } from "react";
import { markActiveViewReady } from "../observability/performance.js";
import { ViewErrorBoundary } from "./ViewErrorBoundary";

type RulesLoader = () => Promise<RuleReadModel[]>;

export function RulesOnlyHub({
  message,
  loadRules,
  loadContent,
}: {
  message: string;
  loadRules: RulesLoader;
  loadContent: () => Promise<InstalledContentModel>;
}) {
  const [activeTab, setActiveTab] = useState<"rules" | "content">("rules");
  useEffect(() => {
    markActiveViewReady(activeTab);
  }, [activeTab]);
  return (
    <div className="information-hub rules-only-hub" data-perspective="player">
      <a className="skip-link" href="#information-content">Skip to information</a>
      <header className="top-bar rules-only-top-bar">
        <div className="brand-lockup" aria-label="Dante's Roleplay">
          <span className="brand-lockup__die" aria-hidden="true">20</span>
          <span className="brand-lockup__copy">
            <strong>Dante&apos;s Roleplay</strong>
            <small>D&amp;D 2024 reference</small>
          </span>
        </div>
        <div className="rules-only-context">
          <span className="eyebrow">Rules library</span>
          <strong>D&amp;D 2024</strong>
        </div>
        <span className="rules-only-status">Reference available</span>
      </header>
      <p className="perspective-notice" role="status">
        {message} Private campaign views remain locked; the Rules library is still available.
      </p>
      <div className="information-hub__body">
        <MainNavigation
          activeTab={activeTab}
          availableTabs={["rules", "content"]}
          chapter="Rules library"
          onSelect={(tab) => {
            if (tab === "rules" || tab === "content") setActiveTab(tab);
          }}
          progress="Private campaign views require authorization"
        />
        <main className="information-content" id="information-content">
          <ViewErrorBoundary key={activeTab} viewLabel={activeTab === "content" ? "Installed Content" : "Rules"}>
            {activeTab === "content"
              ? <InstalledContentView loadContent={loadContent} />
              : <RulesView loadRules={loadRules} rules={[]} />}
          </ViewErrorBoundary>
        </main>
      </div>
    </div>
  );
}
