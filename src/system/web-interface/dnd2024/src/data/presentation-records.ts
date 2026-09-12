import type { WorldFaction, WorldLoreEntry } from "./hub-types";

type JsonObject = Record<string, unknown>;

function object(value: unknown): JsonObject | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as JsonObject : null;
}

function text(value: unknown, fallback = "") {
  return typeof value === "string" ? value : fallback;
}

function strings(value: unknown) {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
}

function links(value: unknown) {
  if (!Array.isArray(value)) return [];
  return value.flatMap((candidate) => {
    const item = object(candidate);
    return item && typeof item.id === "string" && typeof item.name === "string"
      ? [{ id: item.id, name: item.name }]
      : [];
  });
}

function people(value: unknown) {
  if (!Array.isArray(value)) return [];
  return value.flatMap((candidate) => {
    const item = object(candidate);
    return item && typeof item.id === "string" && typeof item.name === "string"
      ? [{ id: item.id, name: item.name, kind: text(item.kind, "Person") }]
      : [];
  });
}

function unavailableFields(value: unknown) {
  const fields = strings(value).filter((field) => field.length > 0 && field.length <= 200);
  return fields.length ? fields : undefined;
}

function faction(value: unknown): WorldFaction | null {
  const item = object(value);
  if (!item || typeof item.id !== "string" || typeof item.name !== "string") return null;
  return {
    id: item.id,
    name: item.name,
    monogram: text(item.monogram, item.name.slice(0, 2).toLocaleUpperCase()),
    influence: text(item.influence, "Influence unavailable"),
    status: text(item.status, "Status unavailable"),
    summary: text(item.summary, "Description unavailable."),
    goals: strings(item.goals),
    methods: strings(item.methods),
    assets: strings(item.assets),
    members: people(item.members),
    territories: links(item.territories).map((link, index) => {
      const source = object(Array.isArray(item.territories) ? item.territories[index] : null);
      return { ...link, region: text(source?.region, "Region unavailable") };
    }),
    relationships: links(item.relationships).map((link, index) => {
      const source = object(Array.isArray(item.relationships) ? item.relationships[index] : null);
      return { ...link, stance: text(source?.stance, "Relationship recorded") };
    }),
    ...(item.kind === "Organization" || item.kind === "Sovereign power" ? { kind: item.kind } : {}),
    ...(typeof item.dmAgenda === "string" ? { dmAgenda: item.dmAgenda } : {}),
    ...(typeof item.dmSecret === "string" ? { dmSecret: item.dmSecret } : {}),
    ...(unavailableFields(item.unavailableFields) ? { unavailableFields: unavailableFields(item.unavailableFields) } : {}),
  };
}

function lore(value: unknown): WorldLoreEntry | null {
  const item = object(value);
  if (!item || typeof item.id !== "string" || typeof item.title !== "string") return null;
  const history = Array.isArray(item.linkedHistory) ? item.linkedHistory.flatMap((candidate) => {
    const link = object(candidate);
    return link && typeof link.id === "string" && typeof link.title === "string"
      ? [{ id: link.id, title: link.title, date: text(link.date, "Date unavailable") }]
      : [];
  }) : [];
  const admissions = Array.isArray(item.admissions) ? item.admissions.flatMap((candidate) => {
    const admission = object(candidate);
    return admission && typeof admission.actorId === "string" && typeof admission.actorName === "string" &&
      typeof admission.stance === "string" && typeof admission.source === "string"
      ? [admission as NonNullable<WorldLoreEntry["admissions"]>[number]] : [];
  }) : [];
  return {
    id: item.id,
    title: item.title,
    category: text(item.category, "World lore"),
    status: text(item.status, "Status unavailable"),
    summary: text(item.summary, "Summary unavailable."),
    body: text(item.body, "Details unavailable."),
    linkedLocations: links(item.linkedLocations),
    linkedPeople: people(item.linkedPeople),
    linkedFactions: links(item.linkedFactions),
    linkedHistory: history,
    ...(typeof item.dmTruth === "string" ? { dmTruth: item.dmTruth } : {}),
    ...(typeof item.dmNote === "string" ? { dmNote: item.dmNote } : {}),
    ...(admissions.length ? { admissions } : {}),
  };
}

function select<T>(value: unknown, project: (value: unknown) => T | null) {
  const source = Array.isArray(value) ? value : [];
  const records = source.flatMap((candidate) => {
    const projected = project(candidate);
    return projected === null ? [] : [projected];
  });
  return { records, omittedCount: source.length - records.length };
}

export const selectWorldFactions = (value: unknown) => select(value, faction);
export const selectWorldLore = (value: unknown) => select(value, lore);

export function selectDisplayRows(value: unknown) {
  return select(value, (candidate) => {
    const item = object(candidate);
    if (!item || typeof item.label !== "string" && typeof item.name !== "string") return null;
    return item;
  });
}
