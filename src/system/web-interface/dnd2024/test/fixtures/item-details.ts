import { contract } from "../../src/server/item-details-validator.js";
import type { ItemDetailsData, ItemDetailsRequest } from "../../src/server/item-view-client";

export const itemRequest: ItemDetailsRequest = { applicationId: "dnd2024-main", stateSpaceId: "state.fixture", campaignId: "campaign.fixture",
  observerId: "actor.fixture", perspective: "player", itemId: "item.staff", contextRevision: "fixture-revision" };
export function itemData(request = itemRequest): ItemDetailsData {
  const special = request.itemId === "item.special", container = request.itemId === "item.pack", unknown = request.itemId === "item.unknown";
  return { version: 1, observerId: request.observerId, itemId: request.itemId, perspective: request.perspective, state: "ready",
    name: unknown ? "Item" : special ? "Ashwood staff of the watch" : container ? "Weathered backpack" : "Travel staff",
    description: unknown ? null : special ? "A repaired ashwood staff, wrapped in blue cord. A faint silver mark circles its grip." : container ? "A sturdy canvas pack with patched leather straps." : "A plain wooden walking staff, worn smooth by years on the road.",
    definitionId: unknown ? null : "definition.fixture", quantity: unknown ? null : 1,
    container: unknown || container ? null : { itemId: "actor.fixture", name: "The traveller", observerKnowledge: null }, equipmentSlots: [],
    properties: unknown ? [] : [{ label: "Weight", value: 4, unit: "lb", sources: [], observerKnowledge: null },
      ...(special ? [{ label: "Recorded durability", value: 0, unit: null, sources: [], observerKnowledge: request.perspective === "dm" ? "known" as const : null },
        { label: "Attuned", value: false, unit: null, sources: [{ label: "The traveller’s recollection", knowledgeState: "suspected" as const }], observerKnowledge: request.perspective === "dm" ? "suspected" as const : null }] : [])],
    sources: unknown ? [] : [{ label: "Fixture equipment record", knowledgeState: "known" }], media: [], reasons: [], observerKnowledge: null };
}
export function itemEnvelope(request = itemRequest, data = itemData(request)) {
  return { applicationId: request.applicationId, stateSpaceId: request.stateSpaceId, qualifiedQueryId: contract.id,
    stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64), outputSchemaHash: contract.outputSchemaHash,
    resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64), data };
}
