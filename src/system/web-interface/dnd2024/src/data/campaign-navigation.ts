export function resolveCampaignWorldTarget<T extends { id: string }>(
  projectedRecords: readonly T[],
  requestedId: string,
): T | null {
  return projectedRecords.find((record) => record.id === requestedId) ?? null;
}
