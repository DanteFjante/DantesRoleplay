"use client";

import { useEffect, useMemo, useRef, useState } from "react";

import type { RuleReadModel } from "../data/hub-types";
import { filterRuleReferences, ruleSectionOptions } from "../data/rules-reference.js";
import { Icon } from "./Icon";

const INITIAL_VISIBLE_RULES = 80;

type RulesLoader = () => Promise<RuleReadModel[]>;

function classificationLabel(classification: RuleReadModel["source"]["classification"]): string {
  return classification === "third-party" ? "Third-party"
    : `${classification[0]?.toLocaleUpperCase() ?? ""}${classification.slice(1)}`;
}

function relatedRule(rules: RuleReadModel[], relatedId: string): RuleReadModel | undefined {
  return rules.find((rule) => rule.id === relatedId)
    ?? rules.find((rule) => `dnd2024.${rule.resolutionKey}` === relatedId);
}

export function RulesView({
  rules: initialRules,
  loadRules,
}: {
  rules: RuleReadModel[];
  loadRules?: RulesLoader;
}) {
  const [rules, setRules] = useState(initialRules);
  const [query, setQuery] = useState("");
  const [sectionId, setSectionId] = useState("");
  const [selectedRuleId, setSelectedRuleId] = useState(initialRules[0]?.id ?? "");
  const [visibleLimit, setVisibleLimit] = useState(INITIAL_VISIBLE_RULES);
  const [refreshing, setRefreshing] = useState(false);
  const [notice, setNotice] = useState("");
  const started = useRef(false);
  const sections = useMemo(() => ruleSectionOptions(rules), [rules]);
  const visibleRules = useMemo(
    () => filterRuleReferences(rules, query, sectionId),
    [rules, query, sectionId],
  );
  const renderedRules = visibleRules.slice(0, visibleLimit);
  const selectedRule = visibleRules.find((rule) => rule.id === selectedRuleId) ?? visibleRules[0] ?? null;

  function selectRule(rule: RuleReadModel) {
    setSelectedRuleId(rule.id);
    window.requestAnimationFrame(() => document.querySelector<HTMLElement>("#rule-detail-heading")?.focus());
  }

  async function refreshRules() {
    if (!loadRules || refreshing) return;
    setRefreshing(true);
    setNotice("");
    try {
      const nextRules = await loadRules();
      setRules(nextRules);
      setSelectedRuleId((current) => nextRules.some((rule) => rule.id === current)
        ? current
        : nextRules[0]?.id ?? "");
      setVisibleLimit(INITIAL_VISIBLE_RULES);
      setNotice(nextRules.length > 0
        ? `${nextRules.length.toLocaleString()} published rules loaded.`
        : "No published readable rules are available for this audience.");
    } catch {
      setNotice("The published rules could not be refreshed. Existing references are still available.");
    } finally {
      setRefreshing(false);
    }
  }

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void refreshRules();
    // The resolved publication is refreshed once whenever the Rules view mounts.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (sectionId && !sections.some((section) => section.id === sectionId)) setSectionId("");
  }, [sectionId, sections]);

  useEffect(() => {
    setVisibleLimit(INITIAL_VISIBLE_RULES);
  }, [query, sectionId]);

  return (
    <div className="supporting-view rules-view">
      <header className="view-intro rules-view__intro">
        <span className="eyebrow">Resolved D&amp;D 2024 reference</span>
        <h1 id="main-view-heading" tabIndex={-1}>Rules</h1>
        <p>Read published guidance from the active core application and its installed extensions. Catalog mechanics and procedures remain authoritative.</p>
      </header>

      {rules.length === 0 && refreshing ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div><h2>Loading published rules</h2><p>Resolving core and extension guidance for this application.</p></div>
        </section>
      ) : rules.length === 0 ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div>
            <h2>No published readable rules</h2>
            <p>The game remains usable, but its current catalog does not publish readable rules for this audience.</p>
            {loadRules ? <button className="rules-refresh" onClick={() => void refreshRules()} type="button">Try again</button> : null}
          </div>
        </section>
      ) : (
        <>
          <nav className="rules-section-nav" aria-label="Rules table of contents">
            <button aria-current={!sectionId ? "page" : undefined} onClick={() => setSectionId("")} type="button">
              All sections
            </button>
            {sections.map((section) => (
              <button
                aria-current={sectionId === section.id ? "page" : undefined}
                key={section.id}
                onClick={() => setSectionId(section.id)}
                type="button"
              >
                {section.label}
                <small>{rules.filter((rule) => rule.section.id === section.id).length}</small>
              </button>
            ))}
          </nav>

          <section className="rules-controls" aria-label="Find a rule">
            <label className="rules-search">
              <span>Search rules</span>
              <span className="rules-search__field">
                <Icon name="Search" size={17} />
                <input
                  autoComplete="off"
                  onChange={(event) => setQuery(event.target.value.slice(0, 100))}
                  placeholder="Search titles, examples, sources, or mechanics"
                  type="search"
                  value={query}
                />
              </span>
            </label>
            <label className="rules-category-filter">
              <span>Section</span>
              <select onChange={(event) => setSectionId(event.target.value)} value={sectionId}>
                <option value="">All sections</option>
                {sections.map((section) => <option key={section.id} value={section.id}>{section.label}</option>)}
              </select>
            </label>
            {loadRules ? (
              <button className="rules-refresh" disabled={refreshing} onClick={() => void refreshRules()} type="button">
                <Icon name="RefreshCw" size={16} />
                {refreshing ? "Refreshing…" : "Refresh rules"}
              </button>
            ) : null}
          </section>

          <div className="rules-results-summary">
            <p className="rules-result-count" aria-live="polite">
              {visibleRules.length.toLocaleString()} {visibleRules.length === 1 ? "rule" : "rules"}
            </p>
            {notice ? <p className="rules-notice" role="status">{notice}</p> : null}
          </div>

          {selectedRule ? (
            <div className="rules-workspace">
              <section className="rules-index" aria-label="Published rules">
                {renderedRules.map((rule) => (
                  <button
                    aria-pressed={selectedRule.id === rule.id}
                    className="rule-index-card"
                    key={rule.id}
                    onClick={() => selectRule(rule)}
                    type="button"
                  >
                    <span className="rule-index-card__icon"><Icon name="BookOpen" size={17} /></span>
                    <span className="rule-index-card__copy">
                      <small>{rule.section.label}</small>
                      <strong>{rule.title}</strong>
                      <span>{rule.summary}</span>
                      <span className={`rule-source-badge rule-source-badge--${rule.source.classification}`}>
                        {classificationLabel(rule.source.classification)} · {rule.source.label}
                      </span>
                    </span>
                    <Icon name="ChevronRight" size={17} />
                  </button>
                ))}
                {renderedRules.length < visibleRules.length ? (
                  <button className="rules-load-more" onClick={() => setVisibleLimit((current) => current + INITIAL_VISIBLE_RULES)} type="button">
                    Show more ({(visibleRules.length - renderedRules.length).toLocaleString()} remaining)
                  </button>
                ) : null}
              </section>

              <article className="rule-detail" aria-live="polite">
                <div className="rule-detail__heading">
                  <span><Icon name="BookOpen" size={23} /></span>
                  <div>
                    <small>{selectedRule.section.label}</small>
                    <h2 id="rule-detail-heading" tabIndex={-1}>{selectedRule.title}</h2>
                    <span className={`rule-source-badge rule-source-badge--${selectedRule.source.classification}`}>
                      {classificationLabel(selectedRule.source.classification)} · {selectedRule.source.label}
                    </span>
                  </div>
                </div>
                <p className="rule-detail__summary">{selectedRule.summary}</p>

                <div className="rule-readable-blocks">
                  {selectedRule.blocks.map((block, index) => (
                    <section className={`rule-readable-block rule-readable-block--${block.kind}`} key={`${block.kind}-${index}`}>
                      {block.heading ? <h3>{block.heading}</h3> : null}
                      {block.body ? <p>{block.body}</p> : null}
                      {block.items.length > 0 ? block.kind === "steps" ? (
                        <ol>{block.items.map((item) => <li key={item}>{item}</li>)}</ol>
                      ) : (
                        <ul>{block.items.map((item) => <li key={item}>{item}</li>)}</ul>
                      ) : null}
                    </section>
                  ))}
                </div>

                {selectedRule.examples.length > 0 ? (
                  <section className="rule-examples">
                    <h3>Examples</h3>
                    {selectedRule.examples.map((example) => (
                      <article key={example.title}><h4>{example.title}</h4><p>{example.body}</p></article>
                    ))}
                  </section>
                ) : null}

                {selectedRule.relatedRuleIds.length > 0 ? (
                  <section className="rule-related">
                    <h3>Related rules</h3>
                    <div>
                      {selectedRule.relatedRuleIds.map((relatedId) => {
                        const related = relatedRule(rules, relatedId);
                        return related ? (
                          <button key={relatedId} onClick={() => {
                            setQuery("");
                            setSectionId(related.section.id);
                            selectRule(related);
                          }} type="button">{related.title}</button>
                        ) : <span key={relatedId}>{relatedId}</span>;
                      })}
                    </div>
                  </section>
                ) : null}

                <footer className="rule-detail__sources">
                  <section>
                    <h3>Sources</h3>
                    <ul>{selectedRule.citations.map((citation) => (
                      <li key={`${citation.sourceId}:${citation.locator}`}><strong>{citation.sourceId}</strong><cite>{citation.locator}</cite></li>
                    ))}</ul>
                  </section>
                  <section>
                    <h3>Authoritative implementation</h3>
                    <ul>
                      {selectedRule.authority.mechanicIds.map((id) => <li key={id}><span>Mechanic</span><code>{id}</code></li>)}
                      {selectedRule.authority.procedureIds.map((id) => <li key={id}><span>Procedure</span><code>{id}</code></li>)}
                    </ul>
                  </section>
                </footer>
              </article>
            </div>
          ) : (
            <section className="rules-empty-state" aria-live="polite">
              <span><Icon name="Search" size={24} /></span>
              <div><h2>No matching rule</h2><p>Try a different search or choose another section.</p></div>
            </section>
          )}
        </>
      )}

      <p className="source-note">Readable rules explain the application. Catalog JavaScript mechanics and procedures remain authoritative.</p>
    </div>
  );
}
