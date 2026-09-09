import { RESOURCE_FRESHNESS_MS, resourceCacheKey } from "../data/resource-policy.ts";
import { ResourceStore, type ResourceInvalidationReason, type ResourceStoreMetrics } from "../data/resource-store.ts";
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
  totalCount: number;
  nextCursor: string | null;
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
  return extensionId && IDENTIFIER.test(extensionId) && displayName && description && kind
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
  const path = typeof summary?.path === "string" && summary.path.length <= 500
    && !/[\u0000-\u001F\u007F]/u.test(summary.path) ? summary.path : null;
  const ownerId = text(item?.ownerId, 63);
  const sourceLabel = text(item?.sourceLabel, 120);
  const sourceClassification = classification(item?.classification);
  if (!id || !name || !description || !kind || !IDENTIFIER.test(kind) || path === null || !ownerId
      || ownerId === "base" || !IDENTIFIER.test(ownerId) || !sourceLabel || !sourceClassification
      || roles.length > 8 || roles.some((role) => !role)
      || typeof item?.isAdditive !== "boolean") return null;
  return {
    id, name, description, kind, path, ownerId, sourceLabel,
    classification: sourceClassification,
    presentationRoles: roles as string[],
    isAdditive: item.isAdditive,
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
    && Number.isSafeInteger(page.totalCount) && (page.totalCount as number) >= 0
    && (page.totalCount as number) <= MAXIMUM_RECORDS
    && (page.totalCount as number) >= page.records.length
    && optionalText(page.nextCursor, MAXIMUM_CURSOR_LENGTH) === page.nextCursor);
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
    throw new ViewReadError(response.status === 409 ? "stale-data" : "transport",
      response.status === 409
        ? "Installed content changed while this page was loading."
        : "Installed content is unavailable.");
  }
  const decoded = await readBoundedJson(response, MAXIMUM_PAGE_BYTES);
  const page = decoded.status === "ready" ? object(decoded.value) : null;
  const pageFingerprint = fingerprint(page?.resolutionFingerprint);
  const pageExtensions = Array.isArray(page?.activeExtensions)
    ? page.activeExtensions.map(extension) : null;
  const rawWinners = Array.isArray(page?.resolvedWinners) ? page.resolvedWinners : null;
  const records = rawWinners?.map(contentRecord) ?? null;
  const availableKinds = Array.isArray(page?.availableKinds)
    ? page.availableKinds.map((kind) => text(kind, 63)) : null;
  const totalCount = page?.totalCount;
  const nextCursor = optionalText(page?.nextCursor, MAXIMUM_CURSOR_LENGTH);
  if (page?.applicationId !== APPLICATION_ID || !pageFingerprint || !pageExtensions
      || pageExtensions.some((value) => !value) || !records || records.some((value) => !value)
      || records.length > PAGE_SIZE || !availableKinds || availableKinds.some((value) => !value || !IDENTIFIER.test(value))
      || !Number.isSafeInteger(totalCount) || (totalCount as number) < records.length
      || (totalCount as number) > MAXIMUM_RECORDS || nextCursor !== page?.nextCursor) {
    throw new ViewReadError("incompatible-data", "Installed content response is invalid.");
  }
  if (normalized.expectedResolutionFingerprint
      && normalized.expectedResolutionFingerprint !== pageFingerprint) {
    throw new ViewReadError("stale-data", "Installed content changed while this page was loading.");
  }
  const result: InstalledContentPage = {
    resolutionFingerprint: pageFingerprint,
    extensions: (pageExtensions as InstalledExtension[]).sort((left, right) =>
      left.displayName.localeCompare(right.displayName) || left.extensionId.localeCompare(right.extensionId)),
    records: records as InstalledContentRecord[],
    availableKinds: [...new Set(availableKinds as string[])].sort(),
    totalCount: totalCount as number,
    nextCursor,
  };
  if (!isInstalledContentPage(result))
    throw new ViewReadError("incompatible-data", "Installed content response is invalid.");
  return result;
}

export class InstalledContentClient {
  readonly #store: ResourceStore;
  readonly #page;

  constructor({
    serverOrigin,
    applicationId = APPLICATION_ID,
    fetchImpl = fetch,
  }: {
    serverOrigin: string;
    applicationId?: string;
    fetchImpl?: typeof fetch;
  }) {
    this.#store = new ResourceStore({
      maximumEntries: 24,
      maximumRetainedBytes: 8 * 1024 * 1024,
      diagnosticName: "installed-content-resources",
    });
    this.#page = this.#store.define<InstalledContentRequest, InstalledContentPage>({
      name: "installed-content-page",
      cacheKey: (value) => {
        const request = normalizeRequest(value);
        return resourceCacheKey("effective-content-v2", applicationId, request.ownerId,
          request.kinds.join(","), request.query, request.cursor,
          request.expectedResolutionFingerprint);
      },
      read: (request, signal) => readInstalledContent({
        serverOrigin, applicationId, request, signal, fetchImpl,
      }),
      validate: isInstalledContentPage,
      maximumAgeMs: RESOURCE_FRESHNESS_MS.installedContent,
      maximumEntryBytes: MAXIMUM_PAGE_BYTES,
    });
  }

  async load(request: InstalledContentRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#page.load(normalizeRequest(request), { signal, preferCached })).value;
  }

  invalidate(reason: ResourceInvalidationReason = "manual") {
    this.#page.invalidate(undefined, reason);
  }

  metrics(): ResourceStoreMetrics {
    return this.#store.metrics();
  }
}
