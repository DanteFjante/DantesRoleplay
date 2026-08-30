import type { LocationPerson, WorldLocation } from "../data/hub-types";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";

function PersonCard({ person }: { person: LocationPerson }) {
  return (
    <article className="location-person-card" aria-labelledby={`${person.id}-heading`}>
      <header>
        <span className="location-person-card__portrait">
          <MediaImage fallback={<span aria-hidden="true">{person.initials}</span>} media={person.portrait} />
        </span>
        <div>
          <small>{person.kind} · {person.role}</small>
          <h3 id={`${person.id}-heading`}>{person.name}</h3>
          <p>{person.disposition}</p>
        </div>
      </header>
      <p className="location-person-card__summary">{person.summary}</p>
      <section>
        <h4><Icon name="ScrollText" size={15} /> Background</h4>
        <p>{person.background}</p>
      </section>
      {person.motive || person.dmSecret ? (
        <section className="location-person-card__dm" aria-label={`DM context for ${person.name}`}>
          <span><Icon name="Shield" size={16} /> DM context</span>
          {person.motive ? <p><strong>Motive</strong>{person.motive}</p> : null}
          {person.dmSecret ? <p><strong>Secret</strong>{person.dmSecret}</p> : null}
        </section>
      ) : null}
    </article>
  );
}

export function LocationPeople({ location }: { location: WorldLocation }) {
  return (
    <section className="location-detail location-people" aria-labelledby="location-people-heading">
      <header className="location-detail__header">
        <div>
          <span className="eyebrow">Present at {location.name}</span>
          <h2 id="location-people-heading">People &amp; creatures</h2>
          <p>Known faces and observed inhabitants in this location.</p>
        </div>
        <span className="location-detail__seal" aria-hidden="true">
          <Icon name="UsersRound" size={25} />
        </span>
      </header>

      {location.people.length ? (
        <div className="location-people-grid">
          {location.people.map((person) => <PersonCard key={person.id} person={person} />)}
        </div>
      ) : (
        <div className="location-content-empty">
          <Icon name="UsersRound" size={24} />
          <strong>No one is currently listed here</strong>
          <p>This location has no known occupants in the current view.</p>
        </div>
      )}
    </section>
  );
}
