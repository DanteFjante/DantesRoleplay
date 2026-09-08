import type { PartyMemberReadModel } from "../../data/hub-types";
import { CharacterIdentityText, CharacterPortrait } from "./CharacterIdentitySummary";

export function CharacterHero({ member }: { member: PartyMemberReadModel }) {
  return (
    <header className="character-hero">
      <span className="character-hero__portrait">
        <CharacterPortrait eager hero member={member} />
      </span>
      <div className="character-hero__identity">
        <span className="eyebrow">{member.isCurrent ? "Your character" : "Party character"}</span>
        <CharacterIdentityText hero member={member} showOrigin />
      </div>
      <dl className="character-hero__status">
        <div><dt>Campaign</dt><dd>{member.status}</dd></div>
        <div><dt>Record</dt><dd>{member.recordStatus}</dd></div>
      </dl>
    </header>
  );
}
