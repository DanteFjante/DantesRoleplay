import type {
  WorldLocation,
} from "../data/hub-types";
import { Icon } from "./Icon";

function ViewIntro({ eyebrow, title, copy }: { eyebrow: string; title: string; copy: string }) {
  return (
    <header className="view-intro">
      <span className="eyebrow">{eyebrow}</span>
      <h1 id="main-view-heading" tabIndex={-1}>{title}</h1>
      <p>{copy}</p>
    </header>
  );
}

export function CurrentViewPreview({
  image,
  location,
}: {
  image: { imageUrl: string; alt: string } | null;
  location: WorldLocation | null;
}) {
  if (!location) {
    return (
      <div className="supporting-view current-scene-view">
        <ViewIntro
          copy="The table's immediate context, without guessing from campaign prose or map selection."
          eyebrow="Exploration"
          title="Current view"
        />
        <section className="current-scene-unavailable" aria-labelledby="current-scene-unavailable-title">
          <span><Icon name="Compass" size={28} /></span>
          <div>
            <h2 id="current-scene-unavailable-title">Current location unavailable</h2>
            <p>The game server has not projected an exact current location for this seat.</p>
          </div>
        </section>
      </div>
    );
  }

  return (
    <div className="supporting-view current-scene-view">
      <ViewIntro
        copy="The table's immediate context, without asking for a recap."
        eyebrow="Exploration"
        title="Current view"
      />
      <section className="current-scene-card">
        <div className={`current-scene-card__visual${image ? " has-image" : ""}`}>
          {image ? <img alt={image.alt} src={image.imageUrl} /> : <Icon name="Compass" size={30} />}
        </div>
        <div className="current-scene-card__copy">
          <span className="eyebrow">{location.region}</span>
          <h2>{location.name}</h2>
          <p>{location.description}</p>
          <div className="current-scene-card__facts" aria-label="Scene facts">
            <span><Icon name="MapPin" size={15} /> {location.kind} · {location.status}</span>
            <span><Icon name="UsersRound" size={15} /> {location.people.length} {location.people.length === 1 ? "person" : "people"} here</span>
            <span><Icon name="Route" size={15} /> {location.routes.length} known {location.routes.length === 1 ? "way" : "ways"} onward</span>
          </div>
        </div>
      </section>
      <div className="current-scene-grid">
        <section className="current-scene-panel" aria-labelledby="current-observations-title">
          <header><Icon name="Eye" size={18} /><h2 id="current-observations-title">What you notice</h2></header>
          {location.observations.length ? (
            <ul>{location.observations.map((observation) => <li key={observation}>{observation}</li>)}</ul>
          ) : <p className="current-scene-empty">No observations have been projected for this place.</p>}
        </section>
        <section className="current-scene-panel" aria-labelledby="current-people-title">
          <header><Icon name="UsersRound" size={18} /><h2 id="current-people-title">People here</h2></header>
          {location.people.length ? (
            <div className="current-scene-people">
              {location.people.map((person) => (
                <article key={person.id}>
                  <span>{person.initials}</span>
                  <div><strong>{person.name}</strong><small>{person.role}</small></div>
                </article>
              ))}
            </div>
          ) : <p className="current-scene-empty">No co-present people are visible in this projection.</p>}
        </section>
        <section className="current-scene-panel" aria-labelledby="current-routes-title">
          <header><Icon name="Route" size={18} /><h2 id="current-routes-title">Known ways onward</h2></header>
          {location.routes.length ? (
            <ul>{location.routes.map((route) => <li key={`${route.destination}-${route.detail}`}><strong>{route.destination}</strong><span>{route.detail}</span></li>)}</ul>
          ) : <p className="current-scene-empty">No known exits have been projected for this place.</p>}
        </section>
      </div>
      {location.dmSecret ? (
        <aside className="current-scene-dm" aria-label="Dungeon Master context">
          <span className="eyebrow">Behind the screen</span>
          <p>{location.dmSecret}</p>
        </aside>
      ) : null}
    </div>
  );
}
