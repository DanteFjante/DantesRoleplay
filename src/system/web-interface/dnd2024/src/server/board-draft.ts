import query from "../../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board-draft.json" with { type: "json" };
import validate from "./encounter-board-draft-validator.js";
import type { TacticalEncounterBoard } from "../data/hub-types";

export type BoardDraftScope = { applicationId: string; stateSpaceId: string; campaignId: string; encounterId: string };
export type BoardDraftInput = { columns: number; rows: number; obstacleCount: number; seed: number; setting: "woodland" | "ruin" | "chamber"; prompt: string };
export type BoardDraft = {
  version: 1; campaignId: string; encounterId: string; locationId: string; expectedBoardRevision: number | null;
  board: Omit<TacticalEncounterBoard, "participants" | "turn"> & { status: "active"; visibility: "public" };
  backgroundRequest: { prompt: string; width: number; height: number; mimeType: "image/png" };
  provider: "catalog-deterministic"; model: "square-layout-v1"; seed: number;
};
export type DraftImage = { sha256: string; mediaType: string; byteLength: number; width: number; height: number };
export type DraftProjection = { data: BoardDraft; sourceRevisionFingerprint: string };
export type PreparedBoard = { proposal: Record<string, unknown>; proposalFingerprint: string; receipt: { id: string }; executionKey: string };
export function backgroundPrompt(draft: BoardDraft): string {
  return `${draft.backgroundRequest.prompt}\nUse this reviewed geometry, with x/y measured from the top-left square: ${JSON.stringify(draft.board.obstacles.map(({ label, area }) => ({ label, ...area })))}. Keep every other square clear. Do not draw grid lines, labels or tokens.`;
}
const hash = (value: unknown): value is string => typeof value === "string" && /^[a-f0-9]{64}$/iu.test(value);
const base = (scope: BoardDraftScope) => `/api/applications/${encodeURIComponent(scope.applicationId)}/state-spaces/${encodeURIComponent(scope.stateSpaceId)}`;
const mechanic = (scope: BoardDraftScope) => `${base(scope)}/mechanics/dnd2024.mechanic.encounter.board.accept`;
function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;
  if (value && typeof value === "object") return `{${Object.entries(value).sort(([a],[b]) => a.localeCompare(b)).map(([key,item]) => `${JSON.stringify(key)}:${canonical(item)}`).join(",")}}`;
  return JSON.stringify(value);
}

async function json(response: Response) {
  if (!response.ok) throw new Error(response.status === 403 ? "This draft is available only to the authorized GM."
    : response.status === 409 ? "The scene or board changed. Generate a fresh draft."
    : `Draft request failed (${response.status}). Your combat state is unchanged unless an acceptance was already submitted.`);
  return response.json();
}

export function validateDraftProjection(value: unknown, scope: BoardDraftScope): value is DraftProjection {
  if (!value || typeof value !== "object") return false;
  const envelope = value as Record<string, unknown>;
  if (envelope.applicationId !== scope.applicationId || envelope.stateSpaceId !== scope.stateSpaceId ||
      envelope.qualifiedQueryId !== query.id || envelope.outputSchemaHash !== query.projection.outputSchemaHash ||
      ![envelope.stateSpaceFingerprint, envelope.resolutionFingerprint, envelope.resultFingerprint, envelope.sourceRevisionFingerprint].every(hash) ||
      !validate(envelope.data)) return false;
  const data = envelope.data as BoardDraft;
  return data.campaignId === scope.campaignId && data.encounterId === scope.encounterId &&
    data.backgroundRequest.width === data.board.columns * 64 && data.backgroundRequest.height === data.board.rows * 64 &&
    data.board.obstacles.every((item) => item.area.x + item.area.width <= data.board.columns && item.area.y + item.area.height <= data.board.rows);
}

export async function generateBoardDraft(scope: BoardDraftScope, input: BoardDraftInput, signal: AbortSignal): Promise<DraftProjection> {
  const parameters = new URLSearchParams({ perspective: "dm", campaignId: scope.campaignId, input: JSON.stringify(input) });
  const result = await json(await fetch(`${base(scope)}/entities/${encodeURIComponent(scope.encounterId)}/read-models/${query.id}?${parameters}`, {
    cache: "no-store", signal: AbortSignal.any([signal, AbortSignal.timeout(30_000)]), headers: { Accept: "application/json" },
  }));
  if (!validateDraftProjection(result, scope)) throw new Error("The draft response did not match its authorized contract.");
  return result;
}

export async function uploadDraftImage(scope: BoardDraftScope, file: File, draft: BoardDraft, signal: AbortSignal): Promise<DraftImage> {
  if (file.type !== "image/png" || file.size === 0 || file.size > 10 * 1024 * 1024) throw new Error("Choose a PNG no larger than 10 MiB.");
  const result = await json(await fetch(`/api/applications/${encodeURIComponent(scope.applicationId)}/visual-drafts`, {
    method: "POST", body: file, cache: "no-store", signal: AbortSignal.any([signal, AbortSignal.timeout(30_000)]), headers: {
      "Content-Type": "image/png", "X-Image-Width": String(draft.backgroundRequest.width), "X-Image-Height": String(draft.backgroundRequest.height) },
  }));
  if (!hash(result.sha256) || result.mediaType !== "image/png" || result.byteLength !== file.size ||
      result.width !== draft.backgroundRequest.width || result.height !== draft.backgroundRequest.height)
    throw new Error(`The background must be exactly ${draft.backgroundRequest.width} × ${draft.backgroundRequest.height} pixels. Keep the grid and try another PNG.`);
  return result;
}

async function digest(value: string) {
  const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(bytes)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

export async function prepareBoard(scope: BoardDraftScope, projection: DraftProjection, image: DraftImage | null, signal: AbortSignal): Promise<PreparedBoard> {
  const draft = projection.data;
  const input = { expectedBoardRevision: draft.expectedBoardRevision, expectedLocationId: draft.locationId, board: draft.board,
    background: image ? { role: "map", visibility: ["player", "dm"], sha256: image.sha256, mimeType: image.mediaType,
      width: image.width, height: image.height, alt: "Reviewed top-down combat map", caption: "Visual background only; structured grid owns geometry.", order: 0,
      provenance: { kind: "original", credit: "GM supplied and reviewed", reviewedOn: new Date().toISOString().slice(0,10), version: 1,
        source: `${draft.provider}/${draft.model}; input:${projection.sourceRevisionFingerprint}; prompt:${await digest(backgroundPrompt(draft))}; background:gm-upload` } } : null };
  const result = await json(await fetch(`${mechanic(scope)}/prepare`, { method: "POST", cache: "no-store", signal,
    headers: { "Content-Type": "application/json" }, body: JSON.stringify({ idempotencyKey: crypto.randomUUID(), roleEntityIds: { campaign: scope.campaignId, encounter: scope.encounterId }, input }),
  }));
  if (result.ready !== true || result.requiresConfirmation !== true || !hash(result.proposalFingerprint) ||
      typeof result.receipt?.id !== "string" || result.proposal?.command !== "propose" || result.proposal.steps?.length !== 1 ||
      result.proposal.steps[0].qualifiedId !== "dnd2024.mechanic.encounter.board.accept" ||
      result.proposal.steps[0].kind !== "action" ||
      canonical(result.proposal.steps[0].dependsOn) !== "[]" ||
      canonical(result.proposal.steps[0].roleBindings) !== canonical({ campaign: scope.campaignId, encounter: scope.encounterId }) ||
      canonical(result.proposal.steps[0].input) !== canonical(input)) throw new Error("The host could not prepare this exact board for confirmation.");
  return { proposal: result.proposal, proposalFingerprint: result.proposalFingerprint, receipt: result.receipt, executionKey: crypto.randomUUID() };
}

// Called only by the separate, explicit GM Accept button. A timeout is not proof of rollback.
export async function acceptBoard(scope: BoardDraftScope, prepared: PreparedBoard, signal: AbortSignal): Promise<void> {
  const result = await json(await fetch(`${mechanic(scope)}/execute`, { method: "POST", cache: "no-store", signal,
    headers: { "Content-Type": "application/json" }, body: JSON.stringify({ resolutionReceiptId: prepared.receipt.id,
      proposalFingerprint: prepared.proposalFingerprint, idempotencyKey: prepared.executionKey, proposal: prepared.proposal }),
  }));
  if (result.successful !== true) throw new Error("Acceptance was not confirmed. Refresh the board before taking another action.");
}
