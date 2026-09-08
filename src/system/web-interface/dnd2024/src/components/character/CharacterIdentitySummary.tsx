import type { PartyMemberReadModel } from "../../data/hub-types";
import { MediaImage } from "../MediaImage";

export function CharacterPortrait({
  member,
  eager = false,
  hero = false,
}: {
  member: PartyMemberReadModel;
  eager?: boolean;
  hero?: boolean;
}) {
  return <MediaImage
    fallback={<span aria-hidden="true" className={hero ? "character-hero__monogram" : undefined}>{member.initials}</span>}
    loading={eager ? "eager" : "lazy"}
    media={member.portrait}
  />;
}

export function CharacterIdentityText({
  member,
  hero = false,
  showOrigin = false,
}: {
  member: PartyMemberReadModel;
  hero?: boolean;
  showOrigin?: boolean;
}) {
  const classLine = member.characterSheet?.classes
    ?.map((entry) => `${entry.class.label} ${entry.level}`)
    .join(" / ") ?? member.detail;
  const origin = member.characterSheet?.origin;
  return hero ? <>
    <h2>{member.name}</h2>
    <p>{classLine}</p>
    {showOrigin && origin ? <small>{origin.species.label} · {origin.background.label}</small> : null}
  </> : <>
    <strong>{member.name}</strong><span>{classLine}</span>
  </>;
}
