import type { ConnectedCampaignEnvelope } from "../data/hub-types";
import { Icon } from "./Icon";

export function ServerCampaignConnected({ connection }: { connection: ConnectedCampaignEnvelope }) {
  const characterEntries = connection.actor.entries.slice(0, 12);
  return (
    <main className="hub-unavailable server-campaign">
      <section aria-labelledby="server-campaign-heading">
        <span className="hub-unavailable__icon"><Icon name="CheckCircle2" size={26} /></span>
        <span className="eyebrow">Connected table</span>
        <h1 id="server-campaign-heading">{connection.campaign.name}</h1>
        <p>Playing as {connection.actor.name}.</p>
        {connection.campaign.premise ? <p className="server-campaign__premise">{connection.campaign.premise}</p> : null}

        {connection.campaign.partyGoals.length > 0 ? (
          <div className="server-campaign__section">
            <h2>Party goals</h2>
            <ul>{connection.campaign.partyGoals.map((goal) => <li key={goal}>{goal}</li>)}</ul>
          </div>
        ) : null}

        {characterEntries.length > 0 ? (
          <div className="server-campaign__section">
            <h2>{connection.actor.name}</h2>
            <ul className="server-campaign__entries">
              {characterEntries.map((entry) => (
                <li key={`${entry.kind}:${entry.key}`}>
                  <strong>{entry.label}</strong>
                  {entry.details ? <span>{entry.details}</span> : null}
                </li>
              ))}
            </ul>
            {connection.actor.entries.length > characterEntries.length ? (
              <p className="hub-unavailable__hint">More character details are stored on the server.</p>
            ) : null}
          </div>
        ) : null}

        <div className="server-campaign__section" aria-labelledby="server-knowledge-heading">
          <h2 id="server-knowledge-heading">Known campaign and world information</h2>
          {connection.knowledge.status === "ready" ? (
            <ul className="server-campaign__entries">
              {connection.knowledge.entries.map((entry, index) => (
                <li key={`${index}:${entry.text}`}>
                  <span className="server-campaign__knowledge-text">{entry.text}</span>
                  <small>{entry.stance}</small>
                </li>
              ))}
            </ul>
          ) : connection.knowledge.status === "empty" ? (
            <p>No campaign or world information has been recorded for this character yet.</p>
          ) : (
            <p className="hub-unavailable__hint">Known campaign information is unavailable right now.</p>
          )}
        </div>

        {connection.knowledge.locations.length > 0 ? (
          <div className="server-campaign__section" aria-labelledby="server-locations-heading">
            <h2 id="server-locations-heading">Known places</h2>
            <ul className="server-campaign__entries">
              {connection.knowledge.locations.map((location) => (
                <li key={location.name}>
                  <strong>{location.name}</strong>
                  <small>{location.entries.length} known {location.entries.length === 1 ? "entry" : "entries"}</small>
                </li>
              ))}
            </ul>
          </div>
        ) : null}

        <p className="hub-unavailable__hint">This companion is reading the existing server campaign; it does not create a campaign or use the Eldervale fixture.</p>
      </section>
    </main>
  );
}
