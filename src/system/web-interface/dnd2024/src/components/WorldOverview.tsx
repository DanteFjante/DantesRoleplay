import type { CampaignReadModel, WorldLocation, WorldReadModel } from "../data/hub-types";
import { Icon } from "./Icon";

export function WorldOverview({
  currentLocation,
  campaign,
  onBrowseLocations,
  world,
}: {
  currentLocation: WorldLocation;
  campaign: CampaignReadModel;
  onBrowseLocations: () => void;
  world: WorldReadModel;
}) {
  return (
    <div className="world-overview">
      <section className="world-hero" aria-labelledby="main-view-heading">
        <div className="world-hero__crest" aria-hidden="true">
          <Icon name="Globe2" size={32} />
        </div>
        <div className="world-hero__copy">
          <span className="eyebrow">Persistent world</span>
          <h1 id="main-view-heading" tabIndex={-1}>{world.name}</h1>
          <p>{world.summary}</p>
          <div className="world-hero__meta">
            <span><Icon name="Clock3" size={15} /> {world.era}</span>
            <span><Icon name="ScrollText" size={15} /> {campaign.title}</span>
          </div>
        </div>
        <div className="world-hero__sigil" aria-hidden="true">E</div>
      </section>

      <section className="world-fact-grid" aria-label="World at a glance">
        {world.facts.map((fact) => (
          <article className="world-fact" key={fact.label}>
            <span>{fact.label}</span>
            <strong>{fact.value}</strong>
            <small>{fact.detail}</small>
          </article>
        ))}
      </section>

      <section className="overview-grid">
        <article className="panel premise-card">
          <div className="panel-heading">
            <div>
              <span className="eyebrow">The world remembers</span>
              <h2>World premise</h2>
            </div>
            <Icon name="Sparkles" className="heading-icon" />
          </div>
          <blockquote>{world.premise}</blockquote>
          <p>
            {world.name} continues beyond any single adventure. Places, people, and consequences belong
            to the world and remain when a new campaign begins.
          </p>
        </article>

        <article className="panel current-place-card">
          <span className="eyebrow">Where the party is now</span>
          <div className="current-place-card__title">
            <span><Icon name="LocateFixed" /></span>
            <div>
              <h2>{currentLocation.name}</h2>
              <p>{currentLocation.region}</p>
            </div>
          </div>
          <p>{currentLocation.summary}</p>
          <button className="text-action" onClick={onBrowseLocations} type="button">
            Open location <Icon name="ArrowRight" size={16} />
          </button>
        </article>
      </section>

      <section aria-labelledby="regions-heading" className="panel regions-panel">
        <div className="panel-heading">
          <div>
            <span className="eyebrow">World atlas</span>
            <h2 id="regions-heading">Regions of {world.name}</h2>
          </div>
          <button className="text-action" onClick={onBrowseLocations} type="button">
            Browse all locations <Icon name="ChevronRight" size={16} />
          </button>
        </div>
        <div className="region-grid">
          {world.regions.map((region, index) => (
            <article className="region-card" key={region.name}>
              <span className="region-card__icon">
                <Icon name={["Mountain", "Castle", "TreePine"][index]} />
              </span>
              <div>
                <strong>{region.name}</strong>
                <p>{region.detail}</p>
              </div>
              <small>{region.count} known places</small>
            </article>
          ))}
        </div>
      </section>
    </div>
  );
}
