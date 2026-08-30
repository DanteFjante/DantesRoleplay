import type { RuleReadModel } from "../data/hub-types";
import { MainNavigation } from "./MainNavigation";
import { RulesView } from "./RulesView";

type RulesLoader = () => Promise<RuleReadModel[]>;
type RuleDetailLoader = (rule: RuleReadModel) => Promise<RuleReadModel | null>;

export function RulesOnlyHub({
  message,
  loadRules,
  loadRuleDetail,
}: {
  message: string;
  loadRules: RulesLoader;
  loadRuleDetail: RuleDetailLoader;
}) {
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
          activeTab="rules"
          availableTabs={["rules"]}
          chapter="Rules library"
          onSelect={() => undefined}
          progress="Private campaign views require authorization"
        />
        <main className="information-content" id="information-content">
          <RulesView loadRuleDetail={loadRuleDetail} loadRules={loadRules} rules={[]} />
        </main>
      </div>
    </div>
  );
}
