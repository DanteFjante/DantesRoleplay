import type { WorldHistoryEvent } from "../data/hub-types";
import { Icon } from "./Icon";

function HistoryEventCard({
  event,
  onOpenLocation,
}: {
  event: WorldHistoryEvent;
  onOpenLocation: (locationId: string) => void;
}) {
  return (
    <article className="history-event" aria-labelledby={`${event.id}-heading`}>
      <div className="history-event__marker" aria-hidden="true">
        <Icon name="Clock3" size={16} />
      </div>
      <div className="history-event__card">
        <header>
          <div className="history-event__date">
            <strong>{event.date}</strong>
            <span>{event.era}</span>
          </div>
          <div className="history-event__badges" aria-label="Event classification">
            <span>{event.category}</span>
            <span>{event.region}</span>
            <span data-status={event.status.toLocaleLowerCase()}>{event.status}</span>
          </div>
        </header>
        <h2 id={`${event.id}-heading`}>{event.title}</h2>
        <p className="history-event__summary">{event.summary}</p>

        <section className="history-event__consequence">
          <span aria-hidden="true"><Icon name="Globe2" size={17} /></span>
          <p><strong>Persistent consequence</strong>{event.consequence}</p>
        </section>

        {event.linkedLocations.length || event.linkedPeople.length ? (
          <footer className="history-event__links">
            {event.linkedLocations.length ? (
              <div>
                <span><Icon name="MapPin" size={14} /> Places</span>
                <div>
                  {event.linkedLocations.map((location) => (
                    <button key={location.id} onClick={() => onOpenLocation(location.id)} type="button">
                      {location.name}<Icon name="ArrowRight" size={13} />
                    </button>
                  ))}
                </div>
              </div>
            ) : null}
            {event.linkedPeople.length ? (
              <div>
                <span><Icon name="UsersRound" size={14} /> People &amp; creatures</span>
                <div>
                  {event.linkedPeople.map((person) => (
                    <span className="history-person-chip" key={person.id}>
                      {person.name}<small>{person.kind}</small>
                    </span>
                  ))}
                </div>
              </div>
            ) : null}
          </footer>
        ) : null}

        {event.dmTruth || event.dmConsequence ? (
          <aside className="history-event__dm" aria-label={`DM context for ${event.title}`}>
            <span><Icon name="Shield" size={16} /> DM context</span>
            {event.dmTruth ? <p><strong>Hidden truth</strong>{event.dmTruth}</p> : null}
            {event.dmConsequence ? (
              <p><strong>What follows</strong>{event.dmConsequence}</p>
            ) : null}
          </aside>
        ) : null}
      </div>
    </article>
  );
}

export function HistoryTimeline({
  events,
  onOpenLocation,
}: {
  events: WorldHistoryEvent[];
  onOpenLocation: (locationId: string) => void;
}) {
  if (!events.length) {
    return (
      <div className="history-empty">
        <Icon name="ScrollText" size={26} />
        <strong>No history matches these filters</strong>
        <p>Try another search, region, or category.</p>
      </div>
    );
  }

  return (
    <div className="history-timeline" aria-label="World history timeline">
      {events.map((event) => (
        <HistoryEventCard event={event} key={event.id} onOpenLocation={onOpenLocation} />
      ))}
    </div>
  );
}
