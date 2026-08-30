import type { LocationHolding, WorldLocation } from "../data/hub-types";
import { Icon } from "./Icon";

function HoldingCard({ holding }: { holding: LocationHolding }) {
  return (
    <article className="holding-card" aria-labelledby={`${holding.id}-heading`}>
      <header>
        <span aria-hidden="true"><Icon name="Shield" size={20} /></span>
        <div>
          <small>{holding.kind} · {holding.status}</small>
          <h3 id={`${holding.id}-heading`}>{holding.name}</h3>
        </div>
      </header>
      <p>{holding.summary}</p>
      <section aria-label={`${holding.name} contents`}>
        <h4>Contents</h4>
        <ul>
          {holding.contents.map((item) => (
            <li key={item.name}>
              <span>{item.quantity}</span>
              <p><strong>{item.name}</strong><small>{item.detail}</small></p>
            </li>
          ))}
        </ul>
      </section>
      <div className="holding-card__note">
        <Icon name="Eye" size={15} />
        <p><strong>DM note</strong>{holding.dmNote}</p>
      </div>
    </article>
  );
}

export function LocationHoldings({ location }: { location: WorldLocation }) {
  const holdings = location.holdings ?? [];

  return (
    <section className="location-detail location-holdings" aria-labelledby="location-holdings-heading">
      <header className="location-detail__header">
        <div>
          <span className="eyebrow">DM-only location data</span>
          <h2 id="location-holdings-heading">Holdings</h2>
          <p>Containers and their known contents at {location.name}.</p>
        </div>
        <span className="location-detail__seal location-detail__seal--dm" aria-hidden="true">
          <Icon name="Shield" size={25} />
        </span>
      </header>

      {holdings.length ? (
        <div className="holding-grid">
          {holdings.map((holding) => <HoldingCard holding={holding} key={holding.id} />)}
        </div>
      ) : (
        <div className="location-content-empty">
          <Icon name="Shield" size={24} />
          <strong>No holdings are recorded here</strong>
          <p>This location has no listed containers in the current fixture.</p>
        </div>
      )}
    </section>
  );
}
