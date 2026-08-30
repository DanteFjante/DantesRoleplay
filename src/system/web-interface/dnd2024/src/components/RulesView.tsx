"use client";

import { useEffect, useMemo, useRef, useState } from "react";

import type { RuleReadModel } from "../data/hub-types";
import { filterRuleReferences, ruleCategoryOptions } from "../data/rules-reference.js";
import { Icon } from "./Icon";

const INITIAL_VISIBLE_RULES = 80;

type RulesLoader = () => Promise<RuleReadModel[]>;
type RuleDetailLoader = (rule: RuleReadModel) => Promise<RuleReadModel | null>;

export function RulesView({
  rules: initialRules,
  loadRules,
  loadRuleDetail,
}: {
  rules: RuleReadModel[];
  loadRules?: RulesLoader;
  loadRuleDetail?: RuleDetailLoader;
}) {
  const [rules, setRules] = useState(initialRules);
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState("All");
  const [selectedRuleId, setSelectedRuleId] = useState(initialRules[0]?.id ?? "");
  const [visibleLimit, setVisibleLimit] = useState(INITIAL_VISIBLE_RULES);
  const [refreshing, setRefreshing] = useState(false);
  const [detailBusy, setDetailBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const started = useRef(false);
  const detailRequest = useRef(0);
  const categoryOptions = useMemo(() => ruleCategoryOptions(rules), [rules]);
  const visibleRules = filterRuleReferences(rules, query, category);
  const renderedRules = visibleRules.slice(0, visibleLimit);
  const selectedRule = visibleRules.find((rule) => rule.id === selectedRuleId) ?? visibleRules[0] ?? null;

  async function selectRule(rule: RuleReadModel) {
    setSelectedRuleId(rule.id);
    if (!loadRuleDetail || (rule.source && rule.revision)) return;
    const requestId = detailRequest.current + 1;
    detailRequest.current = requestId;
    setDetailBusy(true);
    try {
      const detail = await loadRuleDetail(rule);
      if (detailRequest.current !== requestId) return;
      if (!detail) {
        setNotice("That reference detail is not available right now.");
        return;
      }
      setRules((current) => current.map((candidate) => candidate.id === detail.id ? detail : candidate));
      setNotice("");
    } catch {
      if (detailRequest.current === requestId) setNotice("That reference detail is not available right now.");
    } finally {
      if (detailRequest.current === requestId) setDetailBusy(false);
    }
  }

  async function refreshRules() {
    if (!loadRules || refreshing) return;
    setRefreshing(true);
    setNotice("");
    try {
      const nextRules = await loadRules();
      if (nextRules.length === 0) {
        setNotice("The registered rules could not be refreshed. Existing references are still available.");
        return;
      }
      setRules(nextRules);
      setSelectedRuleId((current) => nextRules.some((rule) => rule.id === current)
        ? current
        : nextRules[0]?.id ?? "");
      setVisibleLimit(INITIAL_VISIBLE_RULES);
      setNotice(`${nextRules.length.toLocaleString()} registered references loaded.`);
    } catch {
      setNotice("The registered rules could not be refreshed. Existing references are still available.");
    } finally {
      setRefreshing(false);
    }
  }

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void refreshRules();
    // The catalog is refreshed once whenever the Rules view mounts.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!categoryOptions.includes(category)) setCategory("All");
  }, [category, categoryOptions]);

  useEffect(() => {
    setVisibleLimit(INITIAL_VISIBLE_RULES);
  }, [query, category]);

  useEffect(() => {
    if (!selectedRule || selectedRule.source || detailBusy) return;
    void selectRule(selectedRule);
    // A newly selected index entry loads its exact detail once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedRule?.id, selectedRule?.contentFingerprint]);

  return (
    <div className="supporting-view rules-view">
      <header className="view-intro rules-view__intro">
        <span className="eyebrow">D&amp;D 2024 reference</span>
        <h1 id="main-view-heading" tabIndex={-1}>Rules</h1>
        <p>Browse the active D&amp;D 2024 catalog. New and revised registered entries appear when this view refreshes.</p>
      </header>

      {rules.length === 0 && refreshing ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div><h2>Loading registered rules</h2><p>Collecting the current D&amp;D 2024 reference index.</p></div>
        </section>
      ) : rules.length === 0 ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div>
            <h2>Registered rules are not available</h2>
            <p>The game table is still usable. Try refreshing after the D&amp;D catalog is published.</p>
            {loadRules ? <button className="rules-refresh" onClick={() => void refreshRules()} type="button">Try again</button> : null}
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
                  placeholder="Search names, families, IDs, or sources"
                  type="search"
                  value={query}
                />
              </span>
            </label>
            <div className="rules-category-filter" aria-label="Rule category">
              <span>Category</span>
              <select onChange={(event) => setCategory(event.target.value)} value={category}>
                {categoryOptions.map((option) => <option key={option} value={option}>{option}</option>)}
              </select>
            </div>
            {loadRules ? (
              <button className="rules-refresh" disabled={refreshing} onClick={() => void refreshRules()} type="button">
                <Icon name="RefreshCw" size={16} />
                {refreshing ? "Refreshing…" : "Refresh rules"}
              </button>
            ) : null}
          </section>

          <div className="rules-results-summary">
            <p className="rules-result-count" aria-live="polite">
              {visibleRules.length.toLocaleString()} {visibleRules.length === 1 ? "reference" : "references"}
            </p>
            {notice ? <p className="rules-notice" role="status">{notice}</p> : null}
          </div>

          {selectedRule ? (
            <div className="rules-workspace">
              <section className="rules-index" aria-label="Rule references">
                {renderedRules.map((rule) => (
                  <button
                    aria-pressed={selectedRule.id === rule.id}
                    className="rule-index-card"
                    key={rule.id}
                    onClick={() => void selectRule(rule)}
                    type="button"
                  >
                    <span className="rule-index-card__icon"><Icon name="BookOpen" size={17} /></span>
                    <span className="rule-index-card__copy">
                      <small>{rule.category}{rule.subcategory ? ` · ${rule.subcategory}` : ""}</small>
                      <strong>{rule.title}</strong>
                      <span>{rule.summary}</span>
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

              <article className="rule-detail" aria-busy={detailBusy} aria-live="polite">
                <div className="rule-detail__heading">
                  <span><Icon name="BookOpen" size={23} /></span>
                  <div>
                    <small>{selectedRule.category}{selectedRule.subcategory ? ` · ${selectedRule.subcategory}` : ""}</small>
                    <h2>{selectedRule.title}</h2>
                  </div>
                </div>
                <p>{detailBusy ? "Loading the current registered detail…" : selectedRule.summary}</p>
                <footer>
                  <span>Source</span>
                  {selectedRule.source ? <cite>{selectedRule.source.locator}</cite> : (
                    <cite>{detailBusy ? "Checking the registered source…" : "Source detail unavailable."}</cite>
                  )}
                  {selectedRule.revision ? <small>Catalog revision {selectedRule.revision}</small> : null}
                </footer>
              </article>
            </div>
          ) : (
            <section className="rules-empty-state" aria-live="polite">
              <span><Icon name="Search" size={24} /></span>
              <div><h2>No matching reference</h2><p>Try a different search or choose another category.</p></div>
            </section>
          )}
        </>
      )}

      <p className="source-note">Rules are read-only references. The catalog and its mechanics remain authoritative.</p>
    </div>
  );
}
