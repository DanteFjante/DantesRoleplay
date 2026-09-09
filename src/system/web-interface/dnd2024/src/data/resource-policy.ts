/**
 * Browser resource policy (version 1).
 *
 * Cache keys identify the executable read, not the returned state. They include the
 * application, state space, current workspace generation, actual world/campaign/actor/item
 * selection, audience/knowledge observer, query contract fingerprint, and normalized paging
 * inputs. Result and source revision fingerprints stay with the result; a source revision is
 * part of a key only when it is an explicit continuation precondition.
 *
 * Fresh values return immediately. Expired values may remain visible while the same authorized
 * resource revalidates, and survive a failed refresh. Scope, release, definition, and committed
 * object invalidations remove affected values before a replacement read, so authority changes
 * never flash stale data. Responses are retained only in bounded memory and are never persisted.
 */
export const RESOURCE_POLICY_VERSION = 1;

export const RESOURCE_FRESHNESS_MS = Object.freeze({
  campaignSummary: 30_000,
  campaignDetails: 30_000,
  campaignContext: 30_000,
  factionDirectoryPage: 30_000,
  characterSheet: 30_000,
  characterDetails: 30_000,
  characterInventory: 30_000,
  worldLocationScope: 30_000,
  worldInformation: 30_000,
  currentView: 15_000,
  installedContent: 60_000,
  itemRegistry: 60_000,
  itemDefinition: 60_000,
  itemDetails: 30_000,
  itemRecipes: 30_000,
  itemUses: 30_000,
});

type QueryContract = {
  id: string;
  contentHash?: string;
  outputSchemaHash?: string;
  version?: number;
};

export function resourceContractToken(contract: QueryContract) {
  return [contract.id, contract.version ?? 0,
    contract.contentHash ?? contract.outputSchemaHash ?? "unversioned"].join("@");
}

/** JSON arrays give stable separation without delimiter collisions. Callers normalize objects. */
export function resourceCacheKey(...parts: readonly (string | number | boolean | null)[]) {
  return JSON.stringify([RESOURCE_POLICY_VERSION, ...parts]);
}
