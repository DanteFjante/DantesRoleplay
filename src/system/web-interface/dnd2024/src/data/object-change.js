/**
 * @typedef {{ applicationId: string, stateSpaceId: string }} ObjectChangeBoundary
 * @typedef {{ contractVersion: number, cursor: number, applicationId: string, stateSpaceId: string,
 *   object: { qualifiedId: string, version: number } }} ObjectChangeNotice
 */

/**
 * Accepts only a newer v1 notice for the exact authorized snapshot boundary. This makes replayed,
 * duplicate and out-of-order EventSource frames harmless before any cache can be invalidated.
 * @param {string} data
 * @param {ObjectChangeBoundary} boundary
 * @param {number} lastCursor
 * @returns {ObjectChangeNotice | null}
 */
export function parseObjectChange(data, boundary, lastCursor) {
  let value;
  try { value = JSON.parse(data); }
  catch { return null; }
  if (!value || typeof value !== "object" || value.contractVersion !== 1 ||
      !Number.isSafeInteger(value.cursor) || value.cursor <= lastCursor ||
      value.applicationId !== boundary.applicationId ||
      value.stateSpaceId !== boundary.stateSpaceId ||
      !value.object || typeof value.object !== "object" ||
      typeof value.object.qualifiedId !== "string" || !value.object.qualifiedId ||
      !Number.isSafeInteger(value.object.version) || value.object.version < 1) return null;
  return value;
}

/** @param {string} data @param {number} lastCursor */
export function parseCursorCheckpoint(data, lastCursor) {
  try {
    const value = JSON.parse(data);
    return Number.isSafeInteger(value?.cursor) && value.cursor > lastCursor
      ? value.cursor : lastCursor;
  } catch { return lastCursor; }
}
