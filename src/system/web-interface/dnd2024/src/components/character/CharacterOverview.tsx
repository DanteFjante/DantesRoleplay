import type { CanonicalCharacterData, PartyMemberReadModel, PartySectionId } from "../../data/hub-types";
import { Icon } from "../Icon";

export function CharacterOverview({
  summaryOnly = false,
  member,
  onOpenSection,
}: {
  summaryOnly?: boolean;
  member: PartyMemberReadModel;
  onOpenSection: (section: PartySectionId) => void;
}) {
  const sheet = member.characterSheet;
  const dossier = (sheet as CanonicalCharacterData | undefined)?.dossier ?? null;
  const biography = sheet?.identity?.biography ?? member.backstory[0]?.detail;
  const facts = [
    sheet?.origin ? { label: "Species", value: sheet.origin.species.label } : null,
    sheet?.origin ? { label: "Background", value: sheet.origin.background.label } : null,
    sheet?.body ? { label: "Size", value: sheet.body.size.label } : null,
    sheet?.experience ? { label: "Experience", value: `${sheet.experience.total.toLocaleString()} XP` } : null,
  ].filter(Boolean) as Array<{ label: string; value: string }>;

  return (
    <div className="character-overview">
      <section className="character-overview__lead">
        <div>
          <span className="eyebrow">Character dossier</span>
          <h3>{sheet?.classes?.[0]?.class.label ?? member.name}</h3>
          <p>{summaryOnly
            ? "Observer preview shows the campaign roster without opening private character resources."
            : biography ?? "This character is an active participant, but no biography has been recorded."}</p>
        </div>
        {!summaryOnly ? <div className="character-overview__actions">
          <button onClick={() => onOpenSection("sheet")} type="button"><Icon name="Shield" size={17} /> Character sheet</button>
          <button onClick={() => onOpenSection("inventory")} type="button"><Icon name="PackageOpen" size={17} /> Inventory & wallet</button>
        </div> : null}
      </section>
      {facts.length ? <dl className="character-overview__facts">{facts.map((fact) => (
        <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>
      ))}</dl> : null}
      <section className="character-overview__boundary">
        <Icon name="Shield" size={20} />
        <div>
          <strong>{member.recordStatus}</strong>
          <p>Only information authorized for this perspective appears in the dossier.</p>
          {dossier ? <p>Canonical dossier
            {dossier.definitions ? ` · ${dossier.definitions.length} referenced definitions` : ""}
            {dossier.provenance?.inventoryDepth === undefined ? "" : ` · inventory depth ${dossier.provenance.inventoryDepth}`}
            {dossier.coverage === "partial" ? " · some sections unavailable" : ""}
          </p> : null}
        </div>
      </section>
    </div>
  );
}
