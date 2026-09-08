import type {
  CanonicalCharacterData,
  CanonicalCharacterResult,
  CharacterSheetData,
  CharacterSheetResult,
  PartyDossierEntry,
  PartyMemberReadModel,
  SectionState,
} from "../../data/hub-types";
import { legacyReferenceLabel } from "../../data/legacy-reference-label.ts";

type CharacterReadResult = CharacterSheetResult | CanonicalCharacterResult;

function sheetEntries(memberId: string, sheet: CharacterSheetData): PartyDossierEntry[] {
  const entries: PartyDossierEntry[] = [];
  const dossier = (sheet as CanonicalCharacterData).dossier ?? null;
  for (const membership of sheet.classes ?? []) {
    const definition = dossier?.classes.find((entry) => entry.id === membership.id)?.definition;
    entries.push({
      id: `${memberId}:canonical:class:${membership.id}`,
      kind: "class",
      title: `${membership.class.label} · Level ${membership.level}`,
      detail: definition?.summary ?? (membership.subclass
        ? `${membership.subclass.label} subclass. Stored canonical class membership.`
        : "Stored canonical class membership."),
    });
  }
  if (sheet.hitPoints) entries.push({
    id: `${memberId}:canonical:hit-points`, kind: "vital", title: "Hit Points",
    detail: `${sheet.hitPoints.current} of ${sheet.hitPoints.maximum}.` +
      (sheet.hitPoints.maximumReduction ? ` Maximum reduced by ${sheet.hitPoints.maximumReduction}.` : ""),
  });
  if (sheet.temporaryHitPoints) entries.push({
    id: `${memberId}:canonical:temporary-hit-points`, kind: "vital", title: "Temporary Hit Points",
    detail: String(sheet.temporaryHitPoints.amount),
  });
  for (const ability of sheet.abilities ?? []) entries.push({
    id: `${memberId}:canonical:ability:${ability.ability.id}`,
    kind: "ability score", title: ability.ability.label,
    detail: `Score ${ability.score}; ${ability.modifier >= 0 ? "+" : ""}${ability.modifier} modifier.`,
  });
  for (const speed of sheet.movement ?? []) entries.push({
    id: `${memberId}:canonical:movement:${speed.kind.id}`,
    kind: "movement", title: `${speed.kind.label} speed`,
    detail: `${speed.denominator === 1 ? speed.numerator : `${speed.numerator}/${speed.denominator}`} ${speed.unit.label}.`,
  });
  if (sheet.body) entries.push({
    id: `${memberId}:canonical:size`, kind: "body", title: "Size", detail: sheet.body.size.label,
  });
  if (sheet.experience) entries.push({
    id: `${memberId}:canonical:experience`, kind: "advancement", title: "Experience",
    detail: `${sheet.experience.total} recorded XP.`,
  });
  for (const proficiency of sheet.proficiencies ?? []) entries.push({
    id: `${memberId}:canonical:proficiency:${proficiency.proficiency.id}`,
    kind: "proficiency", title: proficiency.proficiency.label,
    detail: `${proficiency.rank.label} rank.`,
  });
  return entries;
}

function backstoryEntries(memberId: string, sheet: CharacterSheetData): PartyDossierEntry[] {
  const identity = sheet.identity;
  if (!identity) return [];
  return ([
    ["pronouns", "Pronouns", identity.pronouns],
    ["appearance", "Appearance", identity.appearance],
    ["biography", "Biography", identity.biography],
    ["player-notes", "Player notes", identity.playerNotes],
  ] as const).flatMap(([key, title, detail]) => detail ? [{
    id: `${memberId}:canonical:identity:${key}`, kind: "identity", title, detail,
  }] : []);
}

function originEntries(memberId: string, sheet: CharacterSheetData): PartyDossierEntry[] {
  if (!sheet.origin) return [];
  const dossier = "dossier" in sheet ? (sheet as CanonicalCharacterData).dossier : null;
  return [
    {
      id: `${memberId}:canonical:origin:species`, kind: "species", title: sheet.origin.species.label,
      detail: dossier?.origin.species.summary ?? "Stored canonical species selection.",
    },
    {
      id: `${memberId}:canonical:origin:background`, kind: "background", title: sheet.origin.background.label,
      detail: dossier?.origin.background.summary ?? "Stored canonical background selection.",
    },
    ...(dossier?.origin.traits ?? []).map((trait) => ({
      id: `${memberId}:canonical:origin:trait:${trait.key}`, kind: "trait", title: trait.label,
      detail: trait.status === "active" ? "Active canonical origin trait."
        : `Recorded origin trait; ${trait.reason?.replaceAll("-", " ") ?? "executable rules behavior is pending"}.`,
    })),
  ];
}

function inventoryEntries(memberId: string, sheet: CanonicalCharacterData): PartyDossierEntry[] {
  return sheet.inventory.items.map((item) => {
    const definition = sheet.dossier.inventory.definitions.find((entry) => entry.id === item.definition.id);
    const equipped = item.equipmentSlots.length
      ? ` Equipped in ${item.equipmentSlots.map((entry) => entry.label).join(", ")}.` : "";
    return {
      id: `${memberId}:canonical:inventory:${item.id}`,
      kind: item.equipmentSlots.length ? "equipped" : "inventory",
      title: item.name,
      detail: `${definition?.summary ? `${definition.summary} ` : ""}Quantity ${item.quantity}. ` +
        `Placement: ${legacyReferenceLabel(item.slot)}.${equipped}`,
      ...(item.media?.illustration || item.media?.icon
        ? { media: item.media.illustration ?? item.media.icon } : {}),
    };
  });
}

function failedState(result: Exclude<CharacterReadResult, { status: "ready" }>): SectionState<PartyDossierEntry[]> {
  return result.status === "forbidden" ? {
    status: "forbidden", data: null, failureCategory: "authorization",
    diagnosticId: result.diagnosticId, ...(result.errorCode ? { errorCode: result.errorCode } : {}),
  } : {
    status: "error", data: null, failureCategory: result.failureCategory,
    diagnosticId: result.diagnosticId, ...(result.errorCode ? { errorCode: result.errorCode } : {}),
    ...(result.httpStatus === undefined ? {} : { httpStatus: result.httpStatus }),
  };
}

export function projectCharacterSheet(
  summary: PartyMemberReadModel,
  result: CharacterSheetResult,
): PartyMemberReadModel {
  if (result.status !== "ready") return {
    ...summary,
    recordStatus: "Canonical character unavailable",
    sheetStatus: "unavailable",
    sheetState: failedState(result),
    sheet: [],
  };
  const sheet = result.data;
  const entries = sheetEntries(summary.id, sheet);
  const origin = originEntries(summary.id, sheet);
  return {
    ...summary,
    detail: origin[0]?.title ?? entries[0]?.title ?? summary.detail,
    recordStatus: "Canonical character sheet",
    sheetStatus: "canonical",
    sheetState: { status: entries.length ? "ready" : "empty", data: entries, source: "canonical" },
    sheet: entries,
    backstory: backstoryEntries(summary.id, sheet),
    origin,
    characterSheet: sheet,
  };
}

export function projectCharacterDetails(
  summary: PartyMemberReadModel,
  result: CanonicalCharacterResult,
): PartyMemberReadModel {
  if (result.status !== "ready") {
    const state = failedState(result);
    return {
      ...summary,
      detail: "Character details temporarily unavailable",
      recordStatus: "Canonical character unavailable",
      sheetStatus: "unavailable",
      inventoryStatus: "unavailable",
      sheetState: state,
      inventoryState: state,
      sheet: [], inventory: [], backstory: [], origin: [],
    };
  }
  const sheet = result.data;
  const projectedSheet = sheetEntries(summary.id, sheet);
  const origin = originEntries(summary.id, sheet);
  const inventory = inventoryEntries(summary.id, sheet);
  return {
    ...summary,
    detail: origin[0]?.title ?? projectedSheet[0]?.title ?? summary.detail,
    recordStatus: "Canonical character state",
    sheetStatus: "canonical",
    inventoryStatus: inventory.length ? "canonical" : "empty",
    sheetState: { status: projectedSheet.length ? "ready" : "empty", data: projectedSheet, source: "canonical" },
    inventoryState: { status: inventory.length ? "ready" : "empty", data: inventory, source: "canonical" },
    sheet: projectedSheet,
    backstory: backstoryEntries(summary.id, sheet),
    origin,
    inventory,
    characterSheet: sheet,
  };
}
