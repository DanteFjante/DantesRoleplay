"use client";

import { useState } from "react";

import type { RuleReadModel } from "../data/hub-types";
import {
  filterRuleReferences,
  RULE_CATEGORY_OPTIONS,
} from "../data/rules-reference.js";
import { Icon } from "./Icon";

type RuleCategoryFilter = "All" | "Action" | "Reaction";

export function RulesView({ rules }: { rules: RuleReadModel[] }) {
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<RuleCategoryFilter>("All");
  const [selectedRuleId, setSelectedRuleId] = useState(rules[0]?.id ?? "");
  const visibleRules = filterRuleReferences(rules, query, category);
  const selectedRule = visibleRules.find((rule) => rule.id === selectedRuleId) ?? visibleRules[0] ?? null;

  return (
    <div className="supporting-view rules-view">
      <header className="view-intro rules-view__intro">
        <span className="eyebrow">D&amp;D 2024 reference</span>
        <h1 id="main-view-heading" tabIndex={-1}>Rules</h1>
        <p>Fast, table-friendly references drawn only from reviewed SRD 5.2.1 records.</p>
      </header>

      {rules.length === 0 ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div>
            <h2>Reviewed rules are not available</h2>
            <p>The game table is still usable. This reference will return when the published D&amp;D catalog is available.</p>
          </div>
        </section>
      ) : (
        <>
          <section className="rules-controls" aria-label="Find a rule">
            <label className="rules-search">
              <span>Search rules</span>
              <span className="rules-search__field">
                <Icon name="Search" size={17} />
                <input
                  autoComplete="off"
                  onChange={(event) => setQuery(event.target.value.slice(0, 100))}
                  placeholder="Search actions, summaries, or sources"
                  type="search"
                  value={query}
                />
              </span>
            </label>
            <div className="rules-category-filter" aria-label="Rule category">
              <span>Category</span>
              <div>
                {RULE_CATEGORY_OPTIONS.map((option) => (
                  <button
                    aria-pressed={category === option}
                    key={option}
                    onClick={() => setCategory(option)}
                    type="button"
                  >
                    {option}
                  </button>
                ))}
              </div>
            </div>
          </section>

          <p className="rules-result-count" aria-live="polite">
            {visibleRules.length} {visibleRules.length === 1 ? "reference" : "references"}
          </p>

          {selectedRule ? (
            <div className="rules-workspace">
              <section className="rules-index" aria-label="Rule references">
                {visibleRules.map((rule) => (
                  <button
                    aria-pressed={selectedRule.id === rule.id}
                    className="rule-index-card"
                    key={rule.id}
                    onClick={() => setSelectedRuleId(rule.id)}
                    type="button"
                  >
                    <span className="rule-index-card__icon"><Icon name="BookOpen" size={17} /></span>
                    <span className="rule-index-card__copy">
                      <small>{rule.category}</small>
                      <strong>{rule.title}</strong>
                      <span>{rule.summary}</span>
                    </span>
                    <Icon name="ChevronRight" size={17} />
                  </button>
                ))}
              </section>

              <article className="rule-detail" aria-live="polite">
                <div className="rule-detail__heading">
                  <span><Icon name="BookOpen" size={23} /></span>
                  <div>
                    <small>{selectedRule.category}</small>
                    <h2>{selectedRule.title}</h2>
                  </div>
                </div>
                <p>{selectedRule.summary}</p>
                <footer>
                  <span>Source</span>
                  <cite>{selectedRule.source.locator}</cite>
                </footer>
              </article>
            </div>
          ) : (
            <section className="rules-empty-state" aria-live="polite">
              <span><Icon name="Search" size={24} /></span>
              <div>
                <h2>No matching reference</h2>
                <p>Try a different search or choose another category.</p>
              </div>
            </section>
          )}
        </>
      )}

      <p className="source-note">Rules are read-only references. The catalog and its mechanics remain authoritative.</p>
    </div>
  );
}
