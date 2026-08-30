import type { CampaignEntityLinks as CampaignEntityLinkModel } from "../data/hub-types";
import { Icon } from "./Icon";

export function CampaignEntityLinks({
  links,
  onOpenFaction,
  onOpenLocation,
  onOpenPerson,
}: {
  links: CampaignEntityLinkModel;
  onOpenFaction?: (factionId: string) => void;
  onOpenLocation: (locationId: string) => void;
  onOpenPerson?: (personId: string) => void;
}) {
  if (!links.locations.length && !links.people.length && !links.factions.length) return null;

  return (
    <div className="campaign-entity-links">
      {links.locations.map((location) => (
        <button
          className="campaign-link"
          key={location.id}
          onClick={() => onOpenLocation(location.id)}
          type="button"
        >
          <Icon name="MapPin" size={14} />
          {location.name}
        </button>
      ))}
      {links.people.map((person) => onOpenPerson ? (
        <button
          className="campaign-link"
          key={person.id}
          onClick={() => onOpenPerson(person.id)}
          type="button"
        >
          <Icon name="CircleUserRound" size={14} />
          {person.name}
        </button>
      ) : (
        <span className="campaign-link campaign-link--reference" key={person.id}>
          <Icon name="CircleUserRound" size={14} />
          {person.name}
        </span>
      ))}
      {links.factions.map((faction) => onOpenFaction ? (
        <button
          className="campaign-link"
          key={faction.id}
          onClick={() => onOpenFaction(faction.id)}
          type="button"
        >
          <Icon name="Shield" size={14} />
          {faction.name}
        </button>
      ) : (
        <span className="campaign-link campaign-link--reference" key={faction.id}>
          <Icon name="Shield" size={14} />
          {faction.name}
        </span>
      ))}
    </div>
  );
}
