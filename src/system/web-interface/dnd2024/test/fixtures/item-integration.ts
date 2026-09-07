import { hubSource } from "../support/hub-source.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { resolveAudience } from "../support/audience-policy.js";
import type { PartyMemberReadModel, Perspective, ReadyHubEnvelope } from "../../src/data/hub-types";
import { itemData, itemEnvelope, itemRequest } from "./item-details";
import { recipeData, recipeEnvelope } from "./item-recipes";
import { usesData, usesEnvelope } from "./item-uses";
import type { ItemDetailsRequest } from "../../src/server/item-view-client";

export const integrationInventory = { kind: "inventory" as const, characterId: itemRequest.observerId, campaignId: itemRequest.campaignId, perspective: "player" as const };
const definition = (id: string) => ({ id, label: id, canonicalName: id, kind: "fixture", status: "identity-only" as const, summary: null, source: null });
export function integrationParty(): PartyMemberReadModel[] {
  return [itemRequest.observerId, "actor.second"].map((id, index) => {
    const member = structuredClone(hubSource.party[0]) as PartyMemberReadModel;
    member.id = id; member.name = index ? "Second traveller" : "The traveller";
    member.inventoryStatus = "canonical";
    member.inventoryState = { status: "ready", source: "canonical", data: [] };
    member.characterSheet = {
      version: 2, subject: { id, label: member.name }, classes: [],
      inventory: { contentsDepth: 4, mayOmitDeeperContents: true, items: [
        { id: "item.pack", name: "Weathered backpack", definition: { id: "definition.pack", label: "Backpack" }, quantity: 1, slot: "carried", parentItemId: null, order: 0, depth: 1, childCount: 2, deeperContentsOmitted: false, equipmentSlots: [] },
        ...["item.staff", "item.unknown"].map((item, order) => ({ id: item, name: order ? "Unidentified item" : "Travel staff", definition: { id: "definition.fixture", label: "Equipment" }, quantity: 1, slot: "contents", parentItemId: "item.pack", order, depth: 2, childCount: 0, deeperContentsOmitted: false, equipmentSlots: [] })),
      ] }, wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
      dossier: {
        origin: { species: definition("fixture.species"), background: definition("fixture.background"), traits: [] },
        classes: [], features: [], definitions: [],
        inventory: { definitions: [], contentsDepth: 4, mayOmitDeeperContents: true },
        levelOneRules: { test: "character-level-one-rules-project", subjectId: id, armorClass: {}, attacks: [], senses: [], savingThrowCircumstances: [], spellAccess: {}, equipment: {}, entitlements: [] },
        provenance: { sheetQueryId: "dnd2024.query.character-sheet-v2", sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project", dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project", definitionCount: 0, inventoryDepth: 4, ruleTextPolicy: "canonical-only" },
      },
    };
    return member;
  });
}
export function integrationEnvelope(perspective: Perspective = "player"): ReadyHubEnvelope {
  const projected = projectHubEnvelope(hubSource, "integration-fixture", resolveAudience({ authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: perspective, dmPrincipalIds: ["dm.fixture"], localSeat: null })) as ReadyHubEnvelope;
  return { ...projected, applicationId: itemRequest.applicationId, stateSpaceId: itemRequest.stateSpaceId, party: integrationParty(),
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: itemRequest.campaignId, worlds: [{ id: projected.world.id, name: projected.world.name, campaigns: [{ id: itemRequest.campaignId, name: "Disposable item verification" }] }] } };
}
export type ItemRead = { tab: "details" | "recipes" | "uses"; request: ItemDetailsRequest; input: Record<string, unknown> };
export function integrationRead(url: string): ItemRead {
  const u = new URL(url, "https://table.test"), parts = u.pathname.split("/"), input = JSON.parse(u.searchParams.get("input")!);
  const tab = parts.at(-1)?.replace("dnd2024.query.inventory-item-", "");
  if (!["details", "recipes", "uses"].includes(tab ?? "")) throw new Error("Unexpected request outside the item read boundary: " + u.pathname);
  return { tab: tab as ItemRead["tab"], input, request: { ...itemRequest, applicationId: parts[3], stateSpaceId: parts[5], observerId: parts[7],
    itemId: input.itemId, perspective: u.searchParams.get("perspective") as Perspective, campaignId: u.searchParams.get("campaignId")! } };
}
export function integrationResponse(read: ItemRead, mode = "ready"): Response {
  if (mode === "stale" || mode === "forbidden" || mode === "unavailable") return new Response(null, { status: mode === "stale" ? 409 : mode === "forbidden" ? 403 : 503 });
  const { request, input, tab } = read;
  if (tab === "details") {
    const data = itemData(request);
    if(request.perspective === "dm") data.description = "DM PRIVATE DETAILS";
    return new Response(JSON.stringify(itemEnvelope(request, data)));
  }
  if (tab === "recipes") {
    const r = { ...request, makesOffset: Number(input.makesOffset), usesOffset: Number(input.usesOffset), expectedSourceRevision: input.expectedSourceRevision as string | null };
    const data = recipeData(r);
    if(request.itemId === "item.unknown") data.makes = data.uses = { state: "partial", entries: [], reasons: ["dependency-unavailable"], nextOffset: null };
    if(request.observerId === "actor.second" || mode === "empty") data.makes = data.uses = { state: "empty", entries: [], reasons: [], nextOffset: null };
    if(request.perspective === "dm") data.makes.entries.forEach(row => { row.description = "DM PRIVATE RECIPE"; });
    return new Response(JSON.stringify(recipeEnvelope(r, data)));
  }
  const r = { ...request, offset: Number(input.offset), expectedSourceRevision: input.expectedSourceRevision as string | null };
  const data = usesData(r);
  if(request.itemId === "item.unknown") data.uses = { state: "partial", entries: [], reasons: ["dependency-unavailable"], nextOffset: null };
  if(request.observerId === "actor.second" || mode === "empty") data.uses = { state: "empty", entries: [], reasons: [], nextOffset: null };
  if(request.perspective === "dm") data.uses.entries.forEach(row => { row.description = "DM PRIVATE USE"; });
  return new Response(JSON.stringify(usesEnvelope(r, data)));
}
