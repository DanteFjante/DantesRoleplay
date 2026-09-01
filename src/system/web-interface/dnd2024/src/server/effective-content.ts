import { normalizeGameServerOrigin } from "./game-server-context.js";

const APPLICATION_ID = "dnd2024";
const PAGE_SIZE = 100;
const MAXIMUM_RECORDS = 20_000;
const MAXIMUM_PAGES = 10_000;
const CLASSIFICATIONS = new Set(["homebrew", "compatibility", "third-party"]);

export type InstalledExtension = {
  extensionId: string;
  displayName: string;
  description: string;
  classification: "homebrew" | "compatibility" | "third-party";
};

export type InstalledContentRecord = {
  id: string;
  name: string;
  description: string;
  kind: string;
  path: string;
  ownerId: string;
  sourceLabel: string;
  classification: "homebrew" | "compatibility" | "third-party";
  presentationRoles: string[];
  isAdditive: boolean;
};

export type InstalledContentModel = {
  resolutionFingerprint: string;
  extensions: InstalledExtension[];
  records: InstalledContentRecord[];
};

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function text(value: unknown, maximum = 500): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
}

function classification(value: unknown): InstalledExtension["classification"] | null {
  return typeof value === "string" && CLASSIFICATIONS.has(value)
    ? value as InstalledExtension["classification"] : null;
}

function extension(value: unknown): InstalledExtension | null {
  const item = object(value);
  const extensionId = text(item?.extensionId, 63);
  const displayName = text(item?.displayName, 120);
  const description = text(item?.description, 2_000);
  const kind = classification(item?.classification);
  return extensionId && displayName && description && kind
    ? { extensionId, displayName, description, classification: kind } : null;
}

function contentRecord(value: unknown): InstalledContentRecord | null {
  const item = object(value);
  const summary = object(item?.record);
  const roles = Array.isArray(item?.presentationRoles)
    ? item.presentationRoles.map((role) => text(role, 100)) : [];
  const id = text(summary?.qualifiedId, 400);
  const name = text(summary?.name, 400);
  const description = text(summary?.description, 5_000);
  const kind = text(summary?.kind, 100);
  const path = text(summary?.path, 500);
  const ownerId = text(item?.ownerId, 63);
  const sourceLabel = text(item?.sourceLabel, 120);
  const sourceClassification = classification(item?.classification);
  if (!id || !name || !description || !kind || !path || !ownerId || ownerId === "base"
      || !sourceLabel || !sourceClassification || roles.some((role) => !role)
      || typeof item?.isAdditive !== "boolean") return null;
  return {
    id, name, description, kind, path, ownerId, sourceLabel,
    classification: sourceClassification,
    presentationRoles: roles as string[],
    isAdditive: item.isAdditive,
  };
}

export async function readInstalledContent({
  serverOrigin,
  applicationId,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  fetchImpl?: typeof fetch;
}): Promise<InstalledContentModel> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID) throw new Error("Installed content is unavailable.");
  const records = new Map<string, InstalledContentRecord>();
  let extensions: InstalledExtension[] | null = null;
  let fingerprint = "";
  let cursor: string | null = null;
  const cursors = new Set<string>();

  for (let pageIndex = 0; pageIndex < MAXIMUM_PAGES; pageIndex += 1) {
    const url = new URL(`/api/applications/${APPLICATION_ID}/content`, `${origin}/`);
    url.searchParams.set("limit", String(PAGE_SIZE));
    if (cursor) url.searchParams.set("cursor", cursor);
    const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store" });
    if (!response.ok) throw new Error("Installed content is unavailable.");
    const page = object(await response.json());
    const pageFingerprint = text(page?.resolutionFingerprint, 64);
    const pageExtensions = Array.isArray(page?.activeExtensions)
      ? page.activeExtensions.map(extension) : null;
    const rawWinners = Array.isArray(page?.resolvedWinners) ? page.resolvedWinners : null;
    const winners = rawWinners?.filter((value) => object(value)?.ownerId !== "base")
      .map(contentRecord) ?? null;
    if (!pageFingerprint || !pageExtensions || pageExtensions.some((value) => !value)
        || !winners || winners.some((value) => !value)) {
      throw new Error("Installed content response is invalid.");
    }
    if (fingerprint && fingerprint !== pageFingerprint) throw new Error("Installed content changed while loading.");
    fingerprint = pageFingerprint;
    extensions ??= pageExtensions as InstalledExtension[];
    if (JSON.stringify(extensions) !== JSON.stringify(pageExtensions)) throw new Error("Extension metadata changed while loading.");
    for (const record of winners as InstalledContentRecord[]) {
      if (records.has(record.id) || records.size >= MAXIMUM_RECORDS) throw new Error("Installed content exceeds its bound.");
      records.set(record.id, record);
    }
    cursor = typeof page?.nextCursor === "string" ? page.nextCursor : null;
    if (!cursor) break;
    if (cursors.has(cursor)) throw new Error("Installed content cursor repeated.");
    cursors.add(cursor);
  }
  if (cursor) throw new Error("Installed content exceeds its page bound.");
  return {
    resolutionFingerprint: fingerprint,
    extensions: (extensions ?? []).sort((left, right) => left.displayName.localeCompare(right.displayName)
      || left.extensionId.localeCompare(right.extensionId)),
    records: [...records.values()].sort((left, right) => left.sourceLabel.localeCompare(right.sourceLabel)
      || left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
  };
}
