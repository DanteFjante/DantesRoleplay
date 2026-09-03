import type { PartyMemberReadModel } from "../../data/hub-types";
import { MediaImage } from "../MediaImage";

export function CharacterHero({ member }: { member: PartyMemberReadModel }) {
  const classLine = member.characterSheet?.classes
    ?.map((entry) => `${entry.class.label} ${entry.level}`)
    .join(" / ") ?? member.detail;
  const origin = member.characterSheet?.origin;
  return (
    <header className="character-hero">
      <span className="character-hero__portrait">
        <MediaImage
          fallback={<span aria-hidden="true" className="character-hero__monogram">{member.initials}</span>}
          loading="eager"
          media={member.portrait}
        />
      </span>
      <div className="character-hero__identity">
        <span className="eyebrow">{member.isCurrent ? "Your character" : "Party character"}</span>
        <h2>{member.name}</h2>
        <p>{classLine}</p>
        {origin ? <small>{origin.species.label} · {origin.background.label}</small> : null}
      </div>
      <dl className="character-hero__status">
        <div><dt>Campaign</dt><dd>{member.status}</dd></div>
        <div><dt>Record</dt><dd>{member.recordStatus}</dd></div>
      </dl>
    </header>
  );
}
