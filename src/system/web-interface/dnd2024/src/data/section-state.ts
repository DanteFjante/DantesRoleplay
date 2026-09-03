import type { PartyMemberReadModel, ReadyHubEnvelope, SectionState } from "./hub-types";

function sameAudienceBoundary(previous: ReadyHubEnvelope, next: ReadyHubEnvelope): boolean {
  const previousCampaignId = previous.contextSelection?.selectedCampaignId ?? previous.revision;
  const nextCampaignId = next.contextSelection?.selectedCampaignId ?? next.revision;
  return previous.applicationId === next.applicationId &&
    previous.stateSpaceId === next.stateSpaceId &&
    previousCampaignId === nextCampaignId &&
    previous.revision === next.revision &&
    previous.audience.seat === next.audience.seat &&
    previous.audience.perspective === next.audience.perspective &&
    previous.audience.allowedPerspectives.length === next.audience.allowedPerspectives.length &&
    previous.audience.allowedPerspectives.every((value, index) =>
      value === next.audience.allowedPerspectives[index]);
}

function isTransientFailure<T>(
  failed: Extract<SectionState<T>, { status: "error" }>,
): boolean {
  if (failed.failureCategory === "transport") return true;
  if (failed.failureCategory !== "http" || failed.httpStatus === undefined) return false;
  return failed.httpStatus === 408 || failed.httpStatus === 429 || failed.httpStatus >= 500;
}

function staleFrom<T>(
  previous: SectionState<T>,
  failed: Extract<SectionState<T>, { status: "error" }>,
): SectionState<T> {
  if (!isTransientFailure(failed)) return failed;
  if ((previous.status !== "ready" && previous.status !== "empty" && previous.status !== "stale") ||
      previous.source !== "canonical") return failed;
  return {
    status: "stale",
    data: previous.data,
    source: previous.source,
    failureCategory: failed.failureCategory,
    diagnosticId: failed.diagnosticId,
    ...(failed.httpStatus === undefined ? {} : { httpStatus: failed.httpStatus }),
  };
}

function preserveMember(previous: PartyMemberReadModel, next: PartyMemberReadModel): PartyMemberReadModel {
  const sheetState = next.sheetState.status === "error"
    ? staleFrom(previous.sheetState, next.sheetState)
    : next.sheetState;
  const inventoryState = next.inventoryState.status === "error"
    ? staleFrom(previous.inventoryState, next.inventoryState)
    : next.inventoryState;
  const sheetIsStale = sheetState.status === "stale";
  const inventoryIsStale = inventoryState.status === "stale";
  return {
    ...next,
    sheetState,
    inventoryState,
    sheet: sheetIsStale ? sheetState.data : next.sheet,
    inventory: inventoryIsStale ? inventoryState.data : next.inventory,
    ...(sheetIsStale ? { sheetStatus: "canonical" as const } : {}),
    ...(inventoryIsStale ? { inventoryStatus: previous.inventoryStatus } : {}),
    ...(sheetIsStale && previous.characterSheet ? { characterSheet: previous.characterSheet } : {}),
    ...(sheetIsStale || inventoryIsStale ? { recordStatus: "Canonical character state is stale" } : {}),
  };
}

export function preserveLastGoodPartyData(
  previous: ReadyHubEnvelope,
  next: ReadyHubEnvelope,
): ReadyHubEnvelope {
  if (!sameAudienceBoundary(previous, next)) return next;
  const previousById = new Map(previous.party.map((member) => [member.id, member]));
  return {
    ...next,
    party: next.party.map((member) => {
      const prior = previousById.get(member.id);
      return prior ? preserveMember(prior, member) : member;
    }),
  };
}
