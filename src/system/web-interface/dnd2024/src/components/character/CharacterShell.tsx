import type { ReactNode } from "react";

import type { PartyMemberReadModel, PartySectionId } from "../../data/hub-types";
import { Icon } from "../Icon";
import { MediaImage } from "../MediaImage";
import { CharacterHero } from "./CharacterHero";

export const CHARACTER_SECTIONS: ReadonlyArray<{ id: PartySectionId; label: string; icon: string }> = [
  { id: "overview", label: "Overview", icon: "CircleUserRound" },
  { id: "sheet", label: "Character", icon: "Shield" },
  { id: "inventory", label: "Inventory", icon: "PackageOpen" },
  { id: "knowledge", label: "Knowledge", icon: "BookOpen" },
  { id: "backstory", label: "Biography", icon: "ScrollText" },
  { id: "origin", label: "Origin", icon: "Sparkles" },
];

export function CharacterShell({
  children,
  onSelectMember,
  onSelectSection,
  party,
  section,
  selectedMember,
}: {
  children: ReactNode;
  onSelectMember: (id: string) => void;
  onSelectSection: (section: PartySectionId) => void;
  party: PartyMemberReadModel[];
  section: PartySectionId;
  selectedMember: PartyMemberReadModel;
}) {
  return (
    <div className="character-page">
      <header className="character-page__heading">
        <div>
          <span className="eyebrow">Campaign companions</span>
          <h1 id="main-view-heading" tabIndex={-1}>The party</h1>
          <p>Authoritative character records for this perspective.</p>
        </div>
        <strong>{party.length} active {party.length === 1 ? "character" : "characters"}</strong>
      </header>

      <div className="character-workspace">
        <aside aria-label="Active party roster" className="character-roster">
          <div className="character-roster__heading">
            <span className="eyebrow">Active roster</span>
            <small>{party.length}</small>
          </div>
          <div className="character-roster__list">
            {party.map((member) => (
              <button
                aria-current={member.id === selectedMember.id ? "true" : undefined}
                className="character-roster__member"
                key={member.id}
                onClick={() => onSelectMember(member.id)}
                type="button"
              >
                <span className="character-roster__portrait">
                  <MediaImage fallback={<span aria-hidden="true">{member.initials}</span>} media={member.portrait} />
                </span>
                <span>
                  <small>{member.isCurrent ? "Current character" : member.recordStatus}</small>
                  <strong>{member.name}</strong>
                  <span>{member.detail}</span>
                </span>
                <Icon name="ChevronRight" size={17} />
              </button>
            ))}
          </div>
        </aside>

        <section aria-label={`${selectedMember.name} character dossier`} className="character-dossier">
          <CharacterHero member={selectedMember} />
          <nav aria-label="Character dossier sections" className="character-tabs">
            {CHARACTER_SECTIONS.map((candidate) => (
              <button
                aria-current={section === candidate.id ? "page" : undefined}
                key={candidate.id}
                onClick={() => onSelectSection(candidate.id)}
                type="button"
              >
                <Icon name={candidate.icon} size={16} />
                {candidate.label}
              </button>
            ))}
          </nav>
          <div className="character-dossier__content">{children}</div>
        </section>
      </div>
    </div>
  );
}
