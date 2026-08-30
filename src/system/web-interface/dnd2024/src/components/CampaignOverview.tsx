import type { CampaignReadModel, CampaignSectionId } from "../data/hub-types";
import { Icon } from "./Icon";

export function CampaignOverview({
  campaign,
  worldName,
  onSectionChange,
}: {
  campaign: CampaignReadModel;
  worldName: string;
  onSectionChange: (section: CampaignSectionId) => void;
}) {
  const latest = [...campaign.adventureLog].sort((left, right) => right.sortOrder - left.sortOrder)[0];

  return (
    <div className="campaign-overview">
      <header className="campaign-hero">
        <div className="campaign-hero__crest"><Icon name="ScrollText" size={31} /></div>
        <div>
          <span className="eyebrow">{campaign.subtitle} · {worldName}</span>
          <h1 id="main-view-heading" tabIndex={-1}>{campaign.title}</h1>
          <p>{campaign.premise}</p>
          <div className="campaign-hero__meta">
            <span><Icon name="Sparkles" size={14} /> {campaign.status}</span>
            <span><Icon name="BookOpen" size={14} /> {campaign.chapter}</span>
          </div>
        </div>
      </header>

      <section aria-label="Campaign summary" className="campaign-fact-grid">
        {campaign.facts.map((fact) => (
          <article className="campaign-fact" key={fact.label}>
            <span>{fact.label}</span>
            <strong>{fact.value}</strong>
            <small>{fact.detail}</small>
          </article>
        ))}
      </section>

      <section className="campaign-overview-grid">
        <article className="panel campaign-question">
          <span className="eyebrow">The question before the party</span>
          <blockquote>{campaign.question}</blockquote>
          <p>{campaign.progress}</p>
        </article>
        <article className="panel campaign-objective-card">
          <div className="panel-heading">
            <div><span className="eyebrow">Active objective</span><h2>What comes next</h2></div>
            <Icon className="heading-icon" name="Compass" />
          </div>
          <strong>{campaign.objective}</strong>
          <p>{campaign.stakes}</p>
          <div className="campaign-milestone">
            <span>Next milestone</span>
            <p>{campaign.nextMilestone}</p>
          </div>
        </article>
      </section>

      <section className="campaign-overview-grid campaign-overview-grid--lower">
        <article className="panel">
          <div className="panel-heading">
            <div><span className="eyebrow">Latest memory</span><h2>{latest?.title ?? "No completed entries yet"}</h2></div>
            <Icon className="heading-icon" name="Clock3" />
          </div>
          {latest
            ? <><p className="campaign-summary-copy">{latest.summary}</p><button className="text-action" onClick={() => onSectionChange("log")} type="button">Read the adventure log <Icon name="ArrowRight" size={15} /></button></>
            : <p className="campaign-summary-copy">Completed chapters and retained session recaps will appear here when the live campaign records them.</p>}
        </article>
        <article className="panel">
          <div className="panel-heading">
            <div><span className="eyebrow">Campaign trail</span><h2>{campaign.placesVisited.length} places remembered</h2></div>
            <Icon className="heading-icon" name="MapPin" />
          </div>
          <p className="campaign-summary-copy">{campaign.placesVisited.length
            ? `Follow the party's path through ${worldName} without turning campaign history into a second copy of the world.`
            : "No explicit visit records exist yet; the page never guesses them from the current location or map."}</p>
          <button className="text-action" onClick={() => onSectionChange("places")} type="button">Browse visited places <Icon name="ArrowRight" size={15} /></button>
        </article>
      </section>

      <section className="campaign-overview-grid campaign-overview-grid--lower">
        <article className="panel">
          <div className="panel-heading">
            <div><span className="eyebrow">Pursuits and pressure</span><h2>{campaign.quests.length} quests · {campaign.threads.length} open threads</h2></div>
            <Icon className="heading-icon" name="Compass" />
          </div>
          <p className="campaign-summary-copy">Keep the party-facing goals, unresolved questions, and next steps in one readable place.</p>
          <div className="campaign-overview-actions"><button className="text-action" onClick={() => onSectionChange("quests")} type="button">Browse quests <Icon name="ArrowRight" size={15} /></button><button className="text-action" onClick={() => onSectionChange("threads")} type="button">Review open threads <Icon name="ArrowRight" size={15} /></button></div>
        </article>
        <article className="panel">
          <div className="panel-heading">
            <div><span className="eyebrow">Party knowledge</span><h2>{campaign.clues.length} recorded clues</h2></div>
            <Icon className="heading-icon" name="Search" />
          </div>
          <p className="campaign-summary-copy">{campaign.clues.length
            ? "Evidence stays separate from conclusions, so the group can see what it knows without an assistant filling the gaps."
            : "No campaign-owned clues are recorded yet. Player-safe setting knowledge remains available in the World tab."}</p>
          <button className="text-action" onClick={() => onSectionChange("clues")} type="button">Browse clues <Icon name="ArrowRight" size={15} /></button>
        </article>
      </section>

      {campaign.dmContext ? (
        <aside className="campaign-dm-banner">
          <span>DM campaign context</span>
          <p>{campaign.dmContext}</p>
        </aside>
      ) : null}
    </div>
  );
}
