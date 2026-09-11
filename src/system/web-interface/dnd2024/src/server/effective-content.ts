import { RequestCoordinator } from "../data/request-coordinator.ts";
import { ViewReadError } from "../data/view-read-client.ts";
import { normalizeGameServerOrigin } from "./game-server-context.js";
import { readBoundedJson } from "./read-model-response.js";

const APPLICATION_ID = "dnd2024";
const PAGE_SIZE = 100;
const MAXIMUM_RECORDS = 20_000;
const MAXIMUM_PAGE_BYTES = 524_288;
const MAXIMUM_CURSOR_LENGTH = 4_096;
const CLASSIFICATIONS = new Set(["homebrew", "compatibility", "third-party"]);
const IDENTIFIER = /^[a-z][a-z0-9-]{0,62}$/u;

export type InstalledExtension = {
  extensionId: string;
  displayName: string;
  description: string | null;
  classification: "homebrew" | "compatibility" | "third-party" | "unknown";
  fieldStatus?: "ready" | "unavailable";
};

export type InstalledContentRecord = {
  id: string;
  name: string;
  description: string | null;
  kind: string;
  path: string | null;
  ownerId: string;
  sourceLabel: string | null;
  classification: "homebrew" | "compatibility" | "third-party" | "unknown";
  presentationRoles: string[];
  isAdditive: boolean | null;
  fieldStatus?: Partial<Record<"name" | "description" | "kind" | "path" | "sourceLabel" | "classification" | "presentationRoles" | "isAdditive", "ready" | "partial" | "unavailable">>;
};

export type InstalledContentRequest = {
  ownerId: string | null;
  kinds: string[];
  query: string;
  cursor: string | null;
  expectedResolutionFingerprint: string | null;
};

export type InstalledContentPage = {
  resolutionFingerprint: string;
  extensions: InstalledExtension[];
  records: InstalledContentRecord[];
  availableKinds: string[];
  totalCount: number | null;
  nextCursor: string | null;
  coverage: "ready" | "partial";
  notices: string[];
};

export const EMPTY_INSTALLED_CONTENT_REQUEST: InstalledContentRequest = Object.freeze({
  ownerId: null,
  kinds: [],
  query: "",
  cursor: null,
  expectedResolutionFingerprint: null,
});

function object(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function text(value: unknown, maximum = 500): string | null {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    && value.trim() === value && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
}

function optionalText(value: unknown, maximum: number): string | null {
  return value === null ? null : text(value, maximum);
}

function fingerprint(value: unknown): string | null {
  return value === "none" || (typeof value === "string" && /^[0-9A-F]{64}$/iu.test(value))
    ? value as string : null;
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
  return extensionId && IDENTIFIER.test(extensionId)
    ? { extensionId, displayName: displayName ?? "Unnamed extension", description,
      classification: kind ?? "unknown", fieldStatus: description && displayName && kind ? "ready" : "unavailable" } : null;
}

function contentRecord(value: unknown): InstalledContentRecord | null {
  const item = object(value);
  const summary = object(item?.record);
  const rawRoles = Array.isArray(item?.presentationRoles) && item.presentationRoles.length <= 8
    ? item.presentationRoles : null;
  const id = text(summary?.qualifiedId, 400);
  const name = text(summary?.name, 400);
  const description = text(summary?.description, 5_000);
  const kind = text(summary?.kind, 100);
  const path = typeof summary?.path === "string" && summary.path.length <= 500
    && !/[\u0000-\u001F\u007F]/u.test(summary.path) ? summary.path : null;
  const ownerId = text(item?.ownerId, 63);
  const sourceLabel = text(item?.sourceLabel, 120);
  const sourceClassification = classification(item?.classification);
  if (!id || !ownerId || ownerId === "base" || !IDENTIFIER.test(ownerId)) return null;
  const roles: string[] = [];
  let rolesPartial = rawRoles === null;
  for (const role of rawRoles ?? []) {
    const candidate = text(role, 100);
    if (!candidate || roles.includes(candidate)) { rolesPartial = true; continue; }
    roles.push(candidate);
  }
  const fieldStatus: NonNullable<InstalledContentRecord["fieldStatus"]> = {};
  if (!description) fieldStatus.description = "unavailable";
  if (!name) fieldStatus.name = "unavailable";
  if (!kind || !IDENTIFIER.test(kind)) fieldStatus.kind = "unavailable";
  if (path === null) fieldStatus.path = "unavailable";
  if (!sourceLabel) fieldStatus.sourceLabel = "unavailable";
  if (!sourceClassification) fieldStatus.classification = "unavailable";
  if (rolesPartial) fieldStatus.presentationRoles = roles.length ? "partial" : "unavailable";
  if (typeof item?.isAdditive !== "boolean") fieldStatus.isAdditive = "unavailable";
  return {
    id, name: name ?? "Unnamed contribution", description, kind: kind && IDENTIFIER.test(kind) ? kind : "type unavailable", path, ownerId, sourceLabel,
    classification: sourceClassification ?? "unknown",
    presentationRoles: roles,
    isAdditive: typeof item?.isAdditive === "boolean" ? item.isAdditive : null,
    fieldStatus,
  };
}

function normalizeRequest(request: InstalledContentRequest): InstalledContentRequest {
  const ownerId = request.ownerId;
  const query = request.query.trim();
  const kinds = [...new Set(request.kinds)].sort();
  const expectedResolutionFingerprint = request.expectedResolutionFingerprint === "none"
    ? "none" : request.expectedResolutionFingerprint?.toUpperCase() ?? null;
  if ((ownerId !== null && !IDENTIFIER.test(ownerId)) || kinds.length > 32
      || kinds.some((kind) => !IDENTIFIER.test(kind)) || query.length > 256
      || /[\u0000-\u001F\u007F]/u.test(query)
      || (request.cursor !== null && optionalText(request.cursor, MAXIMUM_CURSOR_LENGTH) === null)
      || (expectedResolutionFingerprint !== null
        && fingerprint(expectedResolutionFingerprint) === null)) {
    throw new ViewReadError("incompatible-data", "The installed-content request is invalid.");
  }
  return { ...request, ownerId, kinds, query, expectedResolutionFingerprint };
}

/** Exact query identity for Redux ownership; pages from different cursors never merge. */
export function installedContentRequestKey(request: InstalledContentRequest) {
  const normalized = normalizeRequest(request);
  return JSON.stringify([normalized.ownerId, normalized.kinds, normalized.query, normalized.cursor,
    normalized.expectedResolutionFingerprint]);
}

function isInstalledContentPage(value: unknown): value is InstalledContentPage {
  const page = object(value);
  return Boolean(page && fingerprint(page.resolutionFingerprint)
    && Array.isArray(page.extensions) && page.extensions.length <= 64
    && page.extensions.every((item) => extension(item) !== null)
    && Array.isArray(page.records) && page.records.length <= PAGE_SIZE
    && page.records.every((value) => {
      const item = object(value);
      return contentRecord({
        ...item,
        record: {
          qualifiedId: item?.id,
          name: item?.name,
          description: item?.description,
          kind: item?.kind,
          path: item?.path,
        },
      }) !== null;
    })
    && Array.isArray(page.availableKinds) && page.availableKinds.length <= 100
    && page.availableKinds.every((kind) => typeof kind === "string" && IDENTIFIER.test(kind))
    && (page.totalCount === null || Number.isSafeInteger(page.totalCount) && (page.totalCount as number) >= 0
      && (page.totalCount as number) <= MAXIMUM_RECORDS && (page.totalCount as number) >= page.records.length)
    && optionalText(page.nextCursor, MAXIMUM_CURSOR_LENGTH) === page.nextCursor
    && (page.coverage === "ready" || page.coverage === "partial")
    && Array.isArray(page.notices) && page.notices.length <= 64 && page.notices.every((notice) => text(notice, 300) !== null));
}

export async function readInstalledContent({
  serverOrigin,
  applicationId,
  request = EMPTY_INSTALLED_CONTENT_REQUEST,
  signal,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  request?: InstalledContentRequest;
  signal?: AbortSignal;
  fetchImpl?: typeof fetch;
}): Promise<InstalledContentPage> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID)
    throw new ViewReadError("incompatible-data", "Installed content is unavailable.");
  const normalized = normalizeRequest(request);
  const url = new URL(`/api/applications/${APPLICATION_ID}/content`, `${origin}/`);
  url.searchParams.set("limit", String(PAGE_SIZE));
  url.searchParams.set("extensionsOnly", "true");
  if (normalized.ownerId) url.searchParams.set("owner", normalized.ownerId);
  for (const kind of normalized.kinds) url.searchParams.append("kind", kind);
  if (normalized.query) url.searchParams.set("query", normalized.query);
  if (normalized.cursor) url.searchParams.set("cursor", normalized.cursor);
  const response = await fetchImpl(url, {
    headers: { Accept: "application/json" },
    cache: "no-store",
    signal,
  });
  if (!response.ok) {
    throw new ViewReadError(response.status === 401 || response.status === 403 ? "authorization"
      : response.status === 409 ? "stale-data" : "transport",
      response.status === 409
        ? "Installed content changed while this page was loading."
        : response.status === 401 || response.status === 403
          ? "Installed content is not available for this audience."
        : "Installed content is unavailable.");
  }
  const decoded = await readBoundedJson(response, MAXIMUM_PAGE_BYTES);
  const page = decoded.status === "ready" ? object(decoded.value) : null;
  const pageFingerprint = fingerprint(page?.resolutionFingerprint);
  const rawExtensions = Array.isArray(page?.activeExtensions) && page.activeExtensions.length <= 64
    ? page.activeExtensions : null;
  const pageExtensions = rawExtensions?.map(extension) ?? null;
  const rawWinners = Array.isArray(page?.resolvedWinners) ? page.resolvedWinners : null;
  const records = rawWinners?.map(contentRecord) ?? null;
  const rawKinds = Array.isArray(page?.availableKinds) && page.availableKinds.length <= 100
    ? page.availableKinds : null;
  const availableKinds = rawKinds?.map((kind) => text(kind, 63)) ?? null;
  const parsedCursor = optionalText(page?.nextCursor, MAXIMUM_CURSOR_LENGTH);
  if (page?.applicationId !== APPLICATION_ID || !pageFingerprint || !pageExtensions
      || !records || records.length > PAGE_SIZE || !availableKinds) {
    throw new ViewReadError("incompatible-data", "Installed content response is invalid.");
  }
  if (normalized.expectedResolutionFingerprint
      && normalized.expectedResolutionFingerprint !== pageFingerprint) {
    throw new ViewReadError("stale-data", "Installed content changed while this page was loading.");
  }
  const notices: string[] = [];
  const extensions: InstalledExtension[] = [];
  for (const extensionValue of pageExtensions) {
    if (!extensionValue || extensions.some((existing) => existing.extensionId === extensionValue.extensionId)) {
      notices.push("A malformed or duplicate installed extension was omitted."); continue;
    }
    extensions.push(extensionValue);
  }
  const retainedRecords: InstalledContentRecord[] = [];
  for (const record of records) {
    if (!record || retainedRecords.some((existing) => existing.id === record.id)) {
      notices.push("A malformed or duplicate content record was omitted."); continue;
    }
    retainedRecords.push(record);
  }
  const kinds: string[] = [];
  for (const kind of availableKinds) {
    if (!kind || !IDENTIFIER.test(kind) || kinds.includes(kind)) { notices.push("An unavailable content kind was omitted."); continue; }
    kinds.push(kind);
  }
  const totalCount = Number.isSafeInteger(page?.totalCount) && Number(page.totalCount) >= retainedRecords.length
    && Number(page.totalCount) <= MAXIMUM_RECORDS ? Number(page.totalCount) : null;
  if (totalCount === null) notices.push("The total matching-content count is unavailable.");
  const nextCursor = parsedCursor === page?.nextCursor ? parsedCursor : null;
  if (nextCursor === null && page?.nextCursor !== null) notices.push("The next content page could not be safely requested.");
  const result: InstalledContentPage = {
    resolutionFingerprint: pageFingerprint,
    extensions: extensions.sort((left, right) =>
      left.displayName.localeCompare(right.displayName) || left.extensionId.localeCompare(right.extensionId)),
    records: retainedRecords,
    availableKinds: kinds.sort(),
    totalCount,
    nextCursor,
    coverage: notices.length || extensions.some((item) => item.fieldStatus !== "ready") ||
      retainedRecords.some((item) => Object.keys(item.fieldStatus ?? {}).length) ? "partial" : "ready",
    notices,
  };
  if (!isInstalledContentPage(result))
    throw new ViewReadError("incompatible-data", "Installed content response is invalid.");
  return result;
}

export class InstalledContentClient {
  readonly #coordinator = new RequestCoordinator();
  readonly #serverOrigin: string;
  readonly #applicationId: string;
  readonly #fetchImpl: typeof fetch;

  constructor({
    serverOrigin,
    applicationId = APPLICATION_ID,
    fetchImpl = fetch,
  }: {
    serverOrigin: string;
    applicationId?: string;
    fetchImpl?: typeof fetch;
  }) {
    this.#serverOrigin = serverOrigin;
    this.#applicationId = applicationId;
    this.#fetchImpl = fetchImpl;
  }

  async load(request: InstalledContentRequest, signal?: AbortSignal, _preferCached = true) {
    const normalized = normalizeRequest(request);
    return this.#coordinator.load({
      key: (value) => `effective-content-v3\u0000${this.#applicationId}\u0000${JSON.stringify(value)}`,
      read: (value, activeSignal) => readInstalledContent({ serverOrigin: this.#serverOrigin,
        applicationId: this.#applicationId, request: value, signal: activeSignal, fetchImpl: this.#fetchImpl }),
      validate: isInstalledContentPage,
      maximumBytes: MAXIMUM_PAGE_BYTES,
    }, normalized, signal);
  }

  invalidate() { this.#coordinator.invalidate(); }
}
