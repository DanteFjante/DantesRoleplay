import { useEffect, useState } from "react";

import type { InstalledContentModel } from "../server/effective-content";

const BADGE_LABELS = {
  homebrew: "Homebrew",
  compatibility: "Compatibility",
  "third-party": "Third-party",
} as const;

export function InstalledContentView({ loadContent }: { loadContent: () => Promise<InstalledContentModel> }) {
  const [content, setContent] = useState<InstalledContentModel | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    void loadContent().then((value) => {
      if (!controller.signal.aborted) setContent(value);
    }).catch(() => {
      if (!controller.signal.aborted) setError("Installed content could not be loaded.");
    });
    return () => controller.abort();
  }, [loadContent]);

  return (
    <section aria-labelledby="main-view-heading" className="installed-content-view">
      <header className="view-heading installed-content-view__heading">
        <div>
          <span className="eyebrow">Effective application content</span>
          <h1 id="main-view-heading" tabIndex={-1}>Installed content</h1>
          <p>Core rules and active extensions are resolved together. Additions remain visible even when they replace nothing.</p>
        </div>
      </header>
      {error ? <p className="rules-notice" role="alert">{error}</p> : null}
      {!content && !error ? <p className="rules-notice" role="status">Loading installed content…</p> : null}
      {content ? (
        <>
          <div className="installed-extension-grid">
            <article className="installed-extension-card">
              <span className="content-badge content-badge--core">Core</span>
              <h2>D&amp;D 2024 core</h2>
              <p>The active base application content.</p>
            </article>
            {content.extensions.map((item) => (
              <article className="installed-extension-card" key={item.extensionId}>
                <span className={`content-badge content-badge--${item.classification}`}>
                  {BADGE_LABELS[item.classification]}
                </span>
                <h2>{item.displayName}</h2>
                <p>{item.description}</p>
              </article>
            ))}
          </div>
          <div className="installed-content-list">
            <div className="installed-content-list__heading">
              <div>
                <span className="eyebrow">Extension contributions</span>
                <h2>Effective additions and overrides</h2>
              </div>
              <span>{content.records.length} visible</span>
            </div>
            {content.records.length === 0 ? (
              <p className="rules-notice">No active extension records are publicly presentable.</p>
            ) : content.records.map((record) => (
              <article className="installed-content-record" key={record.id}>
                <div>
                  <span className={`content-badge content-badge--${record.classification}`}>
                    {BADGE_LABELS[record.classification]}
                  </span>
                  <span className="content-kind">{record.isAdditive ? "Addition" : "Override"}</span>
                </div>
                <h3>{record.name}</h3>
                <p>{record.description}</p>
                <small>{record.sourceLabel} · {record.presentationRoles.join(" · ")}</small>
              </article>
            ))}
          </div>
        </>
      ) : null}
    </section>
  );
}
