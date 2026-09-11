"use client";

import { useEffect, useMemo, useRef, useState } from "react";

import type { Perspective, RuleReadModel, RulesReferencePublication } from "../data/hub-types";
import { selectRulesPublication, useHubSelector } from "../data/hub-store";
import { navigateItemRoute } from "../data/item-view-route";
import { ViewReadError } from "../data/view-read-client";
import { filterRuleReferences, ruleSectionOptions } from "../data/rules-reference.js";
import { Icon } from "./Icon";

const INITIAL_VISIBLE_RULES = 80;

type RulesLoader = (preferCached?: boolean, signal?: AbortSignal) => Promise<RulesReferencePublication>;

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
  campaignId,
  perspective,
  rulesScope,
}: {
  rules: RuleReadModel[];
  loadRules?: RulesLoader;
  campaignId: string;
  perspective: Perspective;
  /** Connected production view: completed publications come only from Redux. */
  rulesScope?: string;
}) {
  const confirmedPublication = useHubSelector(selectRulesPublication(rulesScope ?? ""));
  const [fallbackPublication, setFallbackPublication] = useState<RulesReferencePublication | null>(null);
  const publication = rulesScope ? confirmedPublication : fallbackPublication;
  const rules = publication?.rules ?? (rulesScope ? [] : initialRules);
  const articleCount = publication?.articleCount ?? null;
  const [query, setQuery] = useState("");
  const [sectionId, setSectionId] = useState("");
  const [selectedRuleId, setSelectedRuleId] = useState(initialRules[0]?.id ?? "");
  const [visibleLimit, setVisibleLimit] = useState(INITIAL_VISIBLE_RULES);
  const [refreshing, setRefreshing] = useState(false);
  const [notice, setNotice] = useState("");
  const refreshingRequest = useRef(0);
  const loadingRef = useRef(false);
  const loaderRef = useRef(loadRules);
  const observedLoader = useRef(loadRules);
  const pendingReload = useRef(false);
  const attemptedScope = useRef<string | undefined>(undefined);
  const [reloadVersion, setReloadVersion] = useState(0);
  loaderRef.current = loadRules;
  const [state, setState] = useState<"loading" | "ready" | "empty" | "error" | "stale">(
    rulesScope ? "loading" : initialRules.length > 0 ? "ready" : loadRules ? "loading" : "empty",
  );
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

  async function refreshRules(preferCached = true, signal?: AbortSignal, replace = false) {
    const loader = loaderRef.current;
    if (!loader || loadingRef.current && !replace) return;
    const request = ++refreshingRequest.current;
    attemptedScope.current = rulesScope;
    loadingRef.current = true;
    let succeeded = false;
    setRefreshing(true);
    setNotice("");
    if (rules.length === 0) setState("loading");
    try {
      const publication = await loader(preferCached, signal);
      if (signal?.aborted || request !== refreshingRequest.current) return;
      const nextRules = publication.rules;
      if (!rulesScope) setFallbackPublication(publication);
      setSelectedRuleId((current) => nextRules.some((rule) => rule.id === current)
        ? current
        : nextRules[0]?.id ?? "");
      setVisibleLimit(INITIAL_VISIBLE_RULES);
      setState(nextRules.length > 0 ? "ready" : "empty");
      setNotice(nextRules.length > 0
        ? publication.articleCount === null
          ? `${nextRules.length.toLocaleString()} readable rules loaded; published total unavailable.`
          : `${publication.articleCount.toLocaleString()} published rules loaded.`
        : "No published readable rules are available for this audience.");
      succeeded = true;
    } catch (error) {
      if (signal?.aborted || request !== refreshingRequest.current || error instanceof ViewReadError && error.category === "cancelled") return;
      const denied = error instanceof ViewReadError && error.category === "authorization";
      const incompatible = error instanceof ViewReadError && error.category === "incompatible-data";
      if (denied) {
        setState("error");
        setNotice("Published rules are not available for this audience.");
      } else if (rules.length > 0) {
        setState("stale");
        setNotice(incompatible
          ? "The rules response changed unexpectedly. The last valid publication is still available."
          : "The published rules could not be refreshed. The last valid publication is still available.");
      } else {
        setState("error");
        setNotice(incompatible
          ? "The published-rules response did not match this site's contract."
          : "The published rules could not be loaded. Check the connection and try again.");
      }
    } finally {
      if (!signal?.aborted && request === refreshingRequest.current) {
        loadingRef.current = false;
        setRefreshing(false);
        // A scope/generation replacement can cancel a shared owner flight
        // without replacing this component's callback until that cancellation
        // settles. Queue one retry after that obsolete attempt is fenced.
        if (pendingReload.current && !succeeded) {
          pendingReload.current = false;
          setReloadVersion((value) => value + 1);
        } else if (succeeded) pendingReload.current = false;
      }
    }
  }

  useEffect(() => {
    if (observedLoader.current === loadRules) return;
    observedLoader.current = loadRules;
    // A callback replacement during a read needs a retry once that obsolete
    // request settles. Do not refetch a just-confirmed initial publication:
    // Dnd can replace its callback while wiring the same owner.
    if (loadingRef.current) {
      pendingReload.current = true;
    } else if (attemptedScope.current === rulesScope && !confirmedPublication) {
      setReloadVersion((value) => value + 1);
    }
  }, [confirmedPublication, loadRules, rulesScope]);

  useEffect(() => {
    const controller = new AbortController();
    void refreshRules(true, controller.signal, true);
    return () => {
      controller.abort();
      if (loadingRef.current) loadingRef.current = false;
    };
    // Scope or a settled owner-generation refresh starts exactly one read.
    // Redux remains the only completed value owner; this just restarts its read.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadVersion, rulesScope]);

  useEffect(() => {
    if (!rulesScope) return;
    if (confirmedPublication) setState(confirmedPublication.rules.length ? "ready" : "empty");
  }, [confirmedPublication, rulesScope]);

  useEffect(() => {
    if (sectionId && !sections.some((section) => section.id === sectionId)) setSectionId("");
  }, [sectionId, sections]);

  useEffect(() => {
    setVisibleLimit(INITIAL_VISIBLE_RULES);
  }, [query, sectionId]);

  function openRelatedContent(content: RuleReadModel["relatedContent"][number]) {
    if (!content.available || !content.collection || !content.contentFingerprint) return;
    if (content.kind === "item") {
      navigateItemRoute({ kind: "registry-item", campaignId, perspective, itemId: content.entityId,
        collection: content.collection, contentFingerprint: content.contentFingerprint, tab: "details" },
      false, null, "rules");
    } else if (content.kind === "recipe") {
      navigateItemRoute({ kind: "registry-recipe", campaignId, perspective, recipeId: content.entityId,
        collection: content.collection, contentFingerprint: content.contentFingerprint }, false, null, "rules");
    }
  }

  return (
    <div className="supporting-view rules-view">
      <header className="view-intro rules-view__intro">
        <span className="eyebrow">Resolved D&amp;D 2024 reference</span>
        <h1 id="main-view-heading" tabIndex={-1}>Rules</h1>
        <p>Read published guidance from the active core application and its installed extensions. Catalog mechanics and procedures remain authoritative.</p>
      </header>

      {state === "loading" ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div><h2>Loading published rules</h2><p>Resolving core and extension guidance for this application.</p></div>
        </section>
      ) : state === "error" ? (
        <section className="rules-empty-state rules-empty-state--error" role="alert">
          <span><Icon name="CircleAlert" size={24} /></span>
          <div><h2>Rules could not be loaded</h2><p>{notice}</p>
            {loadRules ? <button className="rules-refresh" onClick={() => void refreshRules(false)} type="button">Try again</button> : null}
          </div>
        </section>
      ) : state === "empty" ? (
        <section className="rules-empty-state" aria-live="polite">
          <span><Icon name="BookOpen" size={24} /></span>
          <div>
            <h2>No published readable rules</h2>
            <p>The game remains usable, but its current catalog does not publish readable rules for this audience.</p>
            {loadRules ? <button className="rules-refresh" onClick={() => void refreshRules(false)} type="button">Refresh rules</button> : null}
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
              <button className="rules-refresh" disabled={refreshing} onClick={() => void refreshRules(false)} type="button">
                <Icon name="RefreshCw" size={16} />
                {refreshing ? "Refreshing…" : "Refresh rules"}
              </button>
            ) : null}
          </section>

          <div className="rules-results-summary">
            <p className="rules-result-count" aria-live="polite">
              {visibleRules.length.toLocaleString()} {visibleRules.length === 1 ? "rule" : "rules"}
              {articleCount !== null && visibleRules.length !== articleCount
                ? ` from ${articleCount.toLocaleString()} published` : articleCount !== null ? " published" : ""}
            </p>
            {publication?.coverage === "partial" ? <p className="rules-notice" role="status">
              Some readable rule fields were unavailable; displayed records retain only confirmed fields.
            </p> : null}
            {publication?.notices?.map((message) => <p className="rules-notice" key={message} role="status">{message}</p>)}
            {notice ? <p className="rules-notice" role="status">{notice}</p> : null}
          </div>

          {selectedRule ? (
            <div className="rules-workspace">
              <section className="rules-index" aria-label="Published rules">
                {renderedRules.map((rule) => (
                  <button
                    aria-pressed={selectedRule.id === rule.id}
                    className="rule-index-card"
                    data-record-id={rule.id}
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
                {selectedRule.fieldStatus?.summary === "unavailable" ? <p className="rules-notice" role="status">
                  This rule’s summary was unavailable.
                </p> : null}

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
                {selectedRule.fieldStatus?.blocks === "unavailable" ? <p className="rules-notice" role="status">
                  Readable rule detail was unavailable.
                </p> : null}

                {selectedRule.examples.length > 0 ? (
                  <section className="rule-examples">
                    <h3>Examples</h3>
                    {selectedRule.examples.map((example, index) => (
                      <article key={`${example.title}-${index}`}><h4>{example.title}</h4><p>{example.body}</p></article>
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

                {selectedRule.relatedContent.length > 0 ? (
                  <section className="rule-related rule-related--content">
                    <h3>Related content</h3>
                    <div>{selectedRule.relatedContent.map((content) => {
                      const navigable = content.available && (content.kind === "item" || content.kind === "recipe");
                      return navigable ? <button key={`${content.kind}:${content.entityId}`}
                        onClick={() => openRelatedContent(content)} type="button">
                        <Icon name={content.kind === "recipe" ? "CookingPot" : "Package"} size={16} />
                        {content.title}
                      </button> : <span key={`${content.kind}:${content.entityId}`}>
                        {content.title} · unavailable
                      </span>;
                    })}</div>
                  </section>
                ) : null}

                <footer className="rule-detail__sources">
                  <section>
                    <h3>Sources</h3>
                    {selectedRule.citations.length ? <ul>{selectedRule.citations.map((citation) => (
                      <li key={`${citation.sourceId}:${citation.locator}`}><strong>{citation.sourceId}</strong><cite>{citation.locator}</cite></li>
                    ))}</ul> : <p>Source citations unavailable.</p>}
                  </section>
                  <section>
                    <h3>Authoritative implementation</h3>
                    {selectedRule.authority.mechanicIds.length || selectedRule.authority.procedureIds.length ? <ul>
                      {selectedRule.authority.mechanicIds.map((id) => <li key={id}><span>Mechanic</span><code>{id}</code></li>)}
                      {selectedRule.authority.procedureIds.map((id) => <li key={id}><span>Procedure</span><code>{id}</code></li>)}
                    </ul> : <p>Implementation references unavailable.</p>}
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
