import type {
  CanonicalCharacterData,
  CharacterSheetProjectionV2,
  NamedCharacterReference,
} from "../../data/hub-types";

function signed(value: number) {
  return value >= 0 ? `+${value}` : String(value);
}

function SectionHeading({ eyebrow, title }: { eyebrow: string; title: string }) {
  return <header className="character-panel__heading"><span>{eyebrow}</span><h3>{title}</h3></header>;
}

function ReferencePills({ empty, values }: { empty: string; values: NamedCharacterReference[] }) {
  const visible = values.slice(0, 12);
  return visible.length ? (
    <ul className="character-reference-list">
      {visible.map((value) => <li key={value.id}>{value.label}</li>)}
      {values.length > visible.length ? <li>+{values.length - visible.length} more</li> : null}
    </ul>
  ) : <p className="character-panel__empty">{empty}</p>;
}

export function VitalStrip({ sheet }: { sheet: CharacterSheetProjectionV2 }) {
  const vitals = [
    sheet.hitPoints ? { label: "Hit points", value: `${sheet.hitPoints.current} / ${sheet.hitPoints.maximum}` } : null,
    sheet.temporaryHitPoints ? { label: "Temporary", value: String(sheet.temporaryHitPoints.amount) } : null,
    sheet.armorClass ? { label: "Armor class", value: String(sheet.armorClass.value) } : null,
    sheet.initiative ? { label: "Initiative", value: signed(sheet.initiative.modifier) } : null,
    sheet.proficiencyBonus === undefined ? null : { label: "Proficiency", value: signed(sheet.proficiencyBonus) },
    sheet.level === undefined ? null : { label: "Level", value: String(sheet.level) },
  ].filter(Boolean) as Array<{ label: string; value: string }>;
  return (
    <dl aria-label="Combat vitals" className="character-vital-strip">
      {vitals.map((vital) => <div key={vital.label}><dt>{vital.label}</dt><dd>{vital.value}</dd></div>)}
    </dl>
  );
}

export function AbilityScores({ sheet }: { sheet: CharacterSheetProjectionV2 }) {
  if (!sheet.abilities?.length) return null;
  return (
    <section className="character-panel character-panel--abilities">
      <SectionHeading eyebrow="Core scores" title="Abilities" />
      <div className="character-abilities">
        {sheet.abilities.map((entry) => (
          <article key={entry.ability.id}>
            <span>{entry.ability.label}</span>
            <strong>{entry.score}</strong>
            <small>{signed(entry.modifier)}</small>
          </article>
        ))}
      </div>
    </section>
  );
}

export function SavesAndSkills({ sheet }: { sheet: CharacterSheetProjectionV2 }) {
  if (!sheet.savingThrows?.length && !sheet.skills?.length) return null;
  return (
    <section className="character-panel character-panel--checks">
      <SectionHeading eyebrow="Checks" title="Saving throws & skills" />
      <div className="character-checks">
        <div>
          <h4>Saving throws</h4>
          <ul className="character-save-list">
            {sheet.savingThrows?.map((entry) => (
              <li key={entry.ability.id}>
                <span aria-label={entry.proficient ? "Proficient" : "Not proficient"}>{entry.proficient ? "●" : "○"}</span>
                <strong>{entry.ability.label}</strong>
                <b>{signed(entry.modifier)}</b>
              </li>
            ))}
          </ul>
        </div>
        <div>
          <h4>Skills</h4>
          <ul className="character-skill-list">
            {sheet.skills?.map((entry) => (
              <li key={entry.skill.id}>
                <span aria-label={entry.expertise ? "Expertise" : entry.proficient ? "Proficient" : "Not proficient"}>
                  {entry.expertise ? "◆" : entry.proficient ? "●" : "○"}
                </span>
                <strong>{entry.skill.label}</strong>
                <small>{entry.ability.label}</small>
                <b>{signed(entry.modifier)}</b>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}

export function FeatureGroups({ sheet }: { sheet: CanonicalCharacterData }) {
  const hasState = sheet.movement?.length || sheet.senses?.length || sheet.conditions?.length;
  const hasTraining = sheet.proficiencies?.length || sheet.features?.length || sheet.resources?.length;
  if (!hasState && !hasTraining) return null;
  return (
    <section className="character-panel">
      <SectionHeading eyebrow="Capabilities" title="Features & training" />
      <div className="character-feature-groups">
        {sheet.features?.length ? <div><h4>Features</h4><ul>{sheet.features.map((entry) => (
          <li key={`${entry.feature.id}:${entry.grantedBy.id}`}>
            <strong>{entry.feature.label}</strong>
            <span>{entry.grantKind.label}{entry.classLevel ? ` · level ${entry.classLevel}` : ""}</span>
            {sheet.dossier.features.find((detail) => detail.definition.id === entry.feature.id)?.definition.summary
              ? <small>{sheet.dossier.features.find((detail) => detail.definition.id === entry.feature.id)?.definition.summary}</small>
              : null}
            {sheet.dossier.features.find((detail) => detail.definition.id === entry.feature.id)?.implementation.status === "pending"
              ? <small>Rules behavior is not yet implemented.</small>
              : null}
          </li>
        ))}</ul></div> : null}
        {sheet.dossier.origin.traits.length ? <div><h4>Origin traits</h4><ul>{sheet.dossier.origin.traits.map((trait) => (
          <li key={trait.key}><strong>{trait.label}</strong><span>Recorded choice · rules behavior pending</span></li>
        ))}</ul></div> : null}
        {sheet.proficiencies?.length ? <div><h4>Proficiencies</h4><ul>{sheet.proficiencies.map((entry) => (
          <li key={entry.proficiency.id}><strong>{entry.proficiency.label}</strong><span>{entry.rank.label}</span></li>
        ))}</ul></div> : null}
        {sheet.resources?.length ? <div><h4>Resources</h4><ul>{sheet.resources.map((entry) => (
          <li key={entry.id}><strong>{entry.name}</strong><span>{entry.expended} expended</span></li>
        ))}</ul></div> : null}
        {hasState ? <div><h4>Creature state</h4><ul>
          {sheet.movement?.map((entry) => <li key={`move:${entry.kind.id}`}><strong>{entry.kind.label}</strong><span>{entry.numerator / entry.denominator} {entry.unit.label}</span></li>)}
          {sheet.senses?.map((entry) => <li key={`sense:${entry.sense.id}`}><strong>{entry.sense.label}</strong><span>{entry.numerator !== undefined && entry.denominator ? `${entry.numerator / entry.denominator} ${entry.unit?.label ?? ""}` : "Recorded"}</span></li>)}
          {sheet.conditions?.map((entry) => <li key={`condition:${entry.condition.id}`}><strong>{entry.condition.label}</strong><span>{entry.level ? `Level ${entry.level}` : "Active"}</span></li>)}
        </ul></div> : null}
      </div>
    </section>
  );
}

export function Spellbook({ sheet }: { sheet: CharacterSheetProjectionV2 }) {
  if (!sheet.spellcasting?.length) return null;
  return (
    <section className="character-panel">
      <SectionHeading eyebrow="Magic" title="Spellbook" />
      <div className="character-card-grid">
        {sheet.spellcasting.map((entry) => (
          <article key={entry.id}>
            <span>{entry.ability.label} spellcasting</span>
            <h4>{entry.name}</h4>
            <p>{entry.sourceDefinition.label}</p>
            <strong>{entry.preparedSpells.length} prepared</strong>
            <ReferencePills empty="No prepared spells recorded." values={entry.preparedSpells} />
            <strong>{entry.availableSpells.length} available</strong>
            <ReferencePills empty="No available spells recorded." values={entry.availableSpells} />
          </article>
        ))}
      </div>
    </section>
  );
}

export function ActionList({ sheet }: { sheet: CharacterSheetProjectionV2 }) {
  if (!sheet.actions?.length) return null;
  return (
    <section className="character-panel">
      <SectionHeading eyebrow="On your turn" title="Actions" />
      <div className="character-card-grid">
        {sheet.actions.map((entry) => (
          <article key={entry.id}>
            <span>Action source</span>
            <h4>{entry.name}</h4>
            <ReferencePills empty="No activities recorded." values={entry.activities} />
          </article>
        ))}
      </div>
    </section>
  );
}

export function CharacterSheet({ sheet }: { sheet: CanonicalCharacterData }) {
  return (
    <div className="character-sheet-v2">
      <VitalStrip sheet={sheet} />
      <div className="character-sheet-v2__columns">
        <AbilityScores sheet={sheet} />
        <SavesAndSkills sheet={sheet} />
        <FeatureGroups sheet={sheet} />
        <Spellbook sheet={sheet} />
        <ActionList sheet={sheet} />
      </div>
    </div>
  );
}
