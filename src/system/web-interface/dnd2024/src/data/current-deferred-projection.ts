import type { CurrentViewUpdate } from "./object-resources";
import { isVisualMedia } from "../state.js";
import { normalizeCurrentSituation } from "./current-situation.js";

function currentObjectRow(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function safeCurrentVisualMedia(value: unknown): Record<string, unknown> | null {
  const media = currentObjectRow(value);
  const imageUrl = media && Object.hasOwn(media, "imageUrl") ? media.imageUrl : undefined;
  if (!media || !Object.hasOwn(media, "imageUrl") || !Object.hasOwn(media, "alt") ||
      !Object.hasOwn(media, "width") || !Object.hasOwn(media, "height") || !isVisualMedia(media) ||
      typeof imageUrl !== "string" || !imageUrl.startsWith("/api/applications/") || !imageUrl.endsWith("/content")) return null;
  return { imageUrl: media.imageUrl, alt: media.alt, width: media.width, height: media.height };
}

function safeCurrentLocationMedia(value: unknown): { media?: Record<string, unknown>; partial: boolean } {
  if (value === undefined) return { partial: false };
  const media = currentObjectRow(value);
  if (!media) return { partial: true };
  let partial = false;
  const result: Record<string, unknown> = {};
  for (const field of ["portrait", "setting", "map", "illustration", "icon", "scene", "handout"]) {
    if (!Object.hasOwn(media, field)) continue;
    const visual = safeCurrentVisualMedia(media[field]);
    if (visual) result[field] = visual;
    else partial = true;
  }
  return { ...(Object.keys(result).length ? { media: result } : {}), partial };
}

type SafeCurrentLocations = { rows: unknown[]; partial: boolean; incompleteRouteLocationIds: Set<string> };

function currentText(value: unknown, maximum = 4_000): string | null {
  return typeof value === "string" && value.length <= maximum ? value : null;
}

function safeCurrentPeople(value: unknown): { rows: unknown[]; partial: boolean } {
  if (!Array.isArray(value) || value.length > 256) return { rows: [], partial: true };
  const counts = new Map<string, number>();
  for (const candidate of value) {
    const person = currentObjectRow(candidate);
    const personId = currentText(person && Object.hasOwn(person, "id") ? person.id : undefined, 200);
    if (personId) counts.set(personId, (counts.get(personId) ?? 0) + 1);
  }
  let partial = false;
  const rows = value.flatMap((candidate) => {
    const person = currentObjectRow(candidate);
    const personId = currentText(person && Object.hasOwn(person, "id") ? person.id : undefined, 200);
    const name = currentText(person && Object.hasOwn(person, "name") ? person.name : undefined, 400);
    if (!person || !personId || counts.get(personId) !== 1) {
      partial = true;
      return [];
    }
    const portrait = Object.hasOwn(person, "portrait") ? safeCurrentVisualMedia(person.portrait) : null;
    if (Object.hasOwn(person, "portrait") && !portrait) partial = true;
    const initials = currentText(Object.hasOwn(person, "initials") ? person.initials : undefined, 32);
    const role = currentText(Object.hasOwn(person, "role") ? person.role : undefined, 400);
    const kind = Object.hasOwn(person, "kind") && (person.kind === "NPC" || person.kind === "Creature") ? person.kind : null;
    const summary = currentText(Object.hasOwn(person, "summary") ? person.summary : undefined, 4_000);
    const background = currentText(Object.hasOwn(person, "background") ? person.background : undefined, 4_000);
    const disposition = currentText(Object.hasOwn(person, "disposition") ? person.disposition : undefined, 1_000);
    const motive = currentText(Object.hasOwn(person, "motive") ? person.motive : undefined, 4_000);
    const dmSecret = currentText(Object.hasOwn(person, "dmSecret") ? person.dmSecret : undefined, 4_000);
    if (name === null || initials === null || role === null || summary === null || background === null || disposition === null || kind === null ||
        (Object.hasOwn(person, "motive") && motive === null) || (Object.hasOwn(person, "dmSecret") && dmSecret === null)) partial = true;
    return [{ id: personId, name: name ?? "Unnamed person", initials: initials ?? "", role: role ?? "", kind: kind ?? "Type unavailable", summary: summary ?? "",
      background: background ?? "", disposition: disposition ?? "", ...(motive ? { motive } : {}),
      ...(dmSecret ? { dmSecret } : {}), ...(portrait ? { portrait } : {}) }];
  });
  return { rows, partial };
}

function safeCurrentRoutes(value: unknown): { rows: unknown[]; partial: boolean } {
  if (!Array.isArray(value) || value.length > 256) return { rows: [], partial: true };
  let partial = false;
  const rows = value.flatMap((candidate) => {
    const route = currentObjectRow(candidate);
    const destination = currentText(route && Object.hasOwn(route, "destination") ? route.destination : undefined, 400);
    const detail = currentText(route && Object.hasOwn(route, "detail") ? route.detail : undefined, 2_000);
    if (!route || destination === null) {
      partial = true;
      return [];
    }
    if (detail === null) partial = true;
    return [{ destination, detail: detail ?? "Route details unavailable." }];
  });
  return { rows, partial };
}

function safeCurrentObservations(value: unknown): { rows: string[]; partial: boolean } {
  if (!Array.isArray(value) || value.length > 256) return { rows: [], partial: true };
  const rows = value.filter((observation): observation is string => currentText(observation, 4_000) !== null);
  return { rows, partial: rows.length !== value.length };
}

function safeCurrentLocations(value: unknown): SafeCurrentLocations {
  if (!Array.isArray(value) || value.length > 1_000)
    return { rows: [], partial: true, incompleteRouteLocationIds: new Set() };
  let partial = false;
  const incompleteRouteLocationIds = new Set<string>();
  const ids = new Map<string, number>();
  for (const candidate of value) {
    const location = currentObjectRow(candidate);
    const locationId = currentText(location && Object.hasOwn(location, "id") ? location.id : undefined, 200);
    if (locationId) ids.set(locationId, (ids.get(locationId) ?? 0) + 1);
  }
  const rows = value.flatMap((candidate) => {
    const location = currentObjectRow(candidate);
    const locationId = currentText(location && Object.hasOwn(location, "id") ? location.id : undefined, 200);
    const locationName = currentText(location && Object.hasOwn(location, "name") ? location.name : undefined, 400);
    if (!location || !locationId || ids.get(locationId) !== 1) {
      partial = true;
      return [];
    }
    const people = safeCurrentPeople(Object.hasOwn(location, "people") ? location.people : undefined);
    const routes = safeCurrentRoutes(Object.hasOwn(location, "routes") ? location.routes : undefined);
    const observations = safeCurrentObservations(Object.hasOwn(location, "observations") ? location.observations : undefined);
    const locationMedia = safeCurrentLocationMedia(Object.hasOwn(location, "media") ? location.media : undefined);
    const region = currentText(Object.hasOwn(location, "region") ? location.region : undefined, 400);
    const kind = currentText(Object.hasOwn(location, "kind") ? location.kind : undefined, 400);
    const status = currentText(Object.hasOwn(location, "status") ? location.status : undefined, 400);
    const description = currentText(Object.hasOwn(location, "description") ? location.description : undefined, 8_000);
    const atmosphere = currentText(Object.hasOwn(location, "atmosphere") ? location.atmosphere : undefined, 4_000);
    const summary = currentText(Object.hasOwn(location, "summary") ? location.summary : undefined, 4_000);
    const dmSecret = currentText(Object.hasOwn(location, "dmSecret") ? location.dmSecret : undefined, 4_000);
    const parentId = Object.hasOwn(location, "parentId") && (location.parentId === null || typeof location.parentId === "string")
      ? location.parentId : undefined;
    const anchor = currentObjectRow(Object.hasOwn(location, "mapAnchor") ? location.mapAnchor : undefined);
    const anchorX = anchor && typeof anchor.x === "number" ? anchor.x : undefined;
    const anchorY = anchor && typeof anchor.y === "number" ? anchor.y : undefined;
    const mapAnchor = typeof anchorX === "number" && typeof anchorY === "number" && Number.isFinite(anchorX) && Number.isFinite(anchorY) &&
      anchorX >= 0 && anchorX <= 100 && anchorY >= 0 && anchorY <= 100 ? { x: anchorX, y: anchorY } : undefined;
    if (locationName === null || people.partial || routes.partial || observations.partial || locationMedia.partial ||
        region === null || kind === null || status === null || description === null || atmosphere === null || summary === null ||
        (Object.hasOwn(location, "parentId") && parentId === undefined) ||
        (Object.hasOwn(location, "dmSecret") && dmSecret === null) ||
        (Object.hasOwn(location, "mapAnchor") && !mapAnchor)) partial = true;
    if (routes.partial) incompleteRouteLocationIds.add(locationId);
    return [{ id: locationId, name: locationName ?? "Unnamed location", region: region ?? "Region unavailable", kind: kind ?? "Type unavailable",
      status: status ?? "Status unavailable", description: description ?? "", atmosphere: atmosphere ?? "", summary: summary ?? "",
      parentId, ...(mapAnchor ? { mapAnchor } : {}), people: people.rows, routes: routes.rows,
      observations: observations.rows,
      ...(dmSecret ? { dmSecret } : {}),
      ...(locationMedia.media ? { media: locationMedia.media } : {}) }];
  });
  return { rows, partial, incompleteRouteLocationIds };
}

export async function normalizeCurrentViewUpdate(value: unknown): Promise<CurrentViewUpdate | null> {
  if (!value || typeof value !== "object") return null;
  const update = value as Record<string, unknown>;
  if (update.section !== "current") return null;
  const situation = normalizeCurrentSituation(update.currentSituation) ?? {
    status: "unavailable" as const,
    coverage: "partial" as const,
    unavailableFields: ["scene"],
    message: "No authoritative current scene is available.",
  };
  if (!["ready", "unavailable"].includes(String(situation.status))) return null;
  const world = currentObjectRow(update.world);
  const locations = safeCurrentLocations(world && Object.hasOwn(world, "locations") ? world.locations : undefined);
  const currentLocationId = typeof situation.locationId === "string" ? situation.locationId : null;
  const routeCoverage = situation.status === "ready" && situation.kind === "exploration" &&
    currentLocationId !== null && locations.incompleteRouteLocationIds.has(currentLocationId)
    ? "partial" as const : undefined;
  // Current owns the selected scene only. A malformed unrelated World row,
  // holdings, landmarks, or map overlay is not evidence that this scene is
  // incomplete. Route coverage is the one location collection used by Current.
  const currentSituation = routeCoverage
    ? { ...situation, coverage: "partial", unavailableFields: [...new Set([
      ...(Array.isArray(situation.unavailableFields) ? situation.unavailableFields : []), "routes",
    ])] }
    : situation;
  if (routeCoverage && currentSituation.status === "ready" && currentSituation.kind === "exploration")
    currentSituation.routesCoverage = routeCoverage;
  return {
    section: "current",
    currentSituation,
    world: { locations: locations.rows },
    ...(Object.hasOwn(update, "projection") ? { projection: update.projection } : {}),
  } as CurrentViewUpdate;
}
