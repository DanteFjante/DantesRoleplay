import type { WorldLoreEntry } from "../data/hub-types";
import { Icon } from "./Icon";
import { KnowledgeAdmissions } from "./KnowledgeAdmissions";

function LoreLinks({
  entry,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  entry: WorldLoreEntry;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  const hasLinks =
    entry.linkedLocations.length ||
    entry.linkedPeople.length ||
    entry.linkedFactions.length ||
    entry.linkedHistory.length;
  if (!hasLinks) return null;

  return (
    <footer className="lore-card__links">
      {entry.linkedLocations.map((location) => (
        <button key={location.id} onClick={() => onOpenLocation(location.id)} type="button">
          <Icon name="MapPin" size={13} />{location.name}
        </button>
      ))}
      {entry.linkedFactions.map((faction) => (
        <button key={faction.id} onClick={() => onOpenFaction(faction.id)} type="button">
          <Icon name="Shield" size={13} />{faction.name}
        </button>
      ))}
      {entry.linkedHistory.map((event) => (
        <button key={event.id} onClick={onOpenHistory} type="button">
          <Icon name="Clock3" size={13} />{event.title}
        </button>
      ))}
      {entry.linkedPeople.map((person) => (
        <span key={person.id}><Icon name="CircleUserRound" size={13} />{person.name}<small>{person.kind}</small></span>
      ))}
    </footer>
  );
}

export function LoreCard({
  entry,
  onOpenLocation,
  onOpenFaction,
  onOpenHistory,
}: {
  entry: WorldLoreEntry;
  onOpenLocation: (locationId: string) => void;
  onOpenFaction: (factionId: string) => void;
  onOpenHistory: () => void;
}) {
  return (
    <article className="lore-card" aria-labelledby={`${entry.id}-heading`} data-record-id={entry.id}>
      <header>
        <span aria-hidden="true"><Icon name="BookOpen" size={20} /></span>
        <div>
          <small>{entry.category} · {entry.status}</small>
          <h2 id={`${entry.id}-heading`}>{entry.title}</h2>
        </div>
      </header>
      <p className="lore-card__summary">{entry.summary}</p>
      {entry.body && entry.body.trim() !== entry.summary.trim() ? <details>
        <summary>Read full entry</summary><p className="lore-card__body">{entry.body}</p>
      </details> : null}
      <KnowledgeAdmissions admissions={entry.admissions} />
      <LoreLinks
        entry={entry}
        onOpenFaction={onOpenFaction}
        onOpenHistory={onOpenHistory}
        onOpenLocation={onOpenLocation}
      />
      {entry.dmTruth || entry.dmNote ? (
        <aside className="directory-dm-context" aria-label={`DM context for ${entry.title}`}>
          <span><Icon name="Shield" size={15} /> DM context</span>
          {entry.dmTruth ? <p><strong>Hidden truth</strong>{entry.dmTruth}</p> : null}
          {entry.dmNote ? <p><strong>Use at table</strong>{entry.dmNote}</p> : null}
        </aside>
      ) : null}
    </article>
  );
}
