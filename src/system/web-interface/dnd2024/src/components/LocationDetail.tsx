import type { WorldLocation } from "../data/hub-types";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";

export function LocationDetail({
  location,
}: {
  location: WorldLocation;
}) {
  return (
    <article className="location-detail" aria-labelledby="selected-location-heading">
      <header className="location-detail__header">
        <div>
          <span className="eyebrow">{location.region}</span>
          <h2 id="selected-location-heading">{location.name}</h2>
          <p>{location.kind} · {location.status}</p>
        </div>
        <span className="location-detail__seal" aria-hidden="true">
          <Icon name="Landmark" size={25} />
        </span>
      </header>

      {location.media?.setting ? (
        <figure className="location-detail__setting">
          <MediaImage fallback={null} media={location.media.setting} />
        </figure>
      ) : null}

      <p className="location-detail__description">{location.description}</p>
      <div className="atmosphere-line">
        <span>Atmosphere</span>
        <p>{location.atmosphere}</p>
      </div>

      <div className="location-detail__columns">
        <section aria-labelledby="landmarks-heading">
          <h3 id="landmarks-heading"><Icon name="MapPin" size={16} /> Landmarks</h3>
          <ul className="detail-list">
            {location.landmarks.map((landmark) => <li key={landmark}>{landmark}</li>)}
          </ul>
        </section>
        <section aria-labelledby="observations-heading">
          <h3 id="observations-heading"><Icon name="Eye" size={16} /> Observations</h3>
          <ul className="detail-list">
            {location.observations.map((observation) => <li key={observation}>{observation}</li>)}
          </ul>
        </section>
      </div>

      <section aria-labelledby="routes-heading" className="routes-section">
        <h3 id="routes-heading"><Icon name="Route" size={16} /> Known routes</h3>
        <div className="route-list">
          {location.routes.map((route) => (
            <div key={route.destination}>
              <span><Icon name="ArrowRight" size={15} /></span>
              <p><strong>{route.destination}</strong><small>{route.detail}</small></p>
            </div>
          ))}
        </div>
      </section>

      {location.dmSecret ? (
        <section className="dm-insight" aria-labelledby="dm-insight-heading">
          <span><Icon name="Shield" size={18} /></span>
          <div>
            <small>DM secret</small>
            <h3 id="dm-insight-heading">Behind the veil</h3>
            <p>{location.dmSecret}</p>
          </div>
        </section>
      ) : null}
    </article>
  );
}
