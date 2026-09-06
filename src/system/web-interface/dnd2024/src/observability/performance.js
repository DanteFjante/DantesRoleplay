export const PERFORMANCE_MARKS = Object.freeze({
  shellReady: "dnd2024.shell.ready",
  bootstrapResponse: "dnd2024.bootstrap.response",
  activeViewReady: "dnd2024.view.active.ready",
  characterReady: "dnd2024.character.ready",
  mapReady: "dnd2024.map.ready",
  combatBoardReady: "dnd2024.combat-board.ready",
});

export const PERFORMANCE_MEASURES = Object.freeze({
  firstReadyView: "dnd2024.first-ready-view",
});

const recordedMarks = new Set();

function markOnce(name, detail, target = globalThis) {
  if (recordedMarks.has(name) || typeof target.performance?.mark !== "function") return false;
  try {
    target.performance.mark(name, detail === undefined ? undefined : { detail });
    recordedMarks.add(name);
    return true;
  } catch {
    return false;
  }
}

export function markShellReady(target) {
  return markOnce(PERFORMANCE_MARKS.shellReady, undefined, target);
}

export function markBootstrapResponse(status, target) {
  return markOnce(PERFORMANCE_MARKS.bootstrapResponse, { status }, target);
}

export function markActiveViewReady(view, target) {
  const marked = markOnce(PERFORMANCE_MARKS.activeViewReady, { view }, target);
  if (marked && typeof target?.performance?.measure === "function") {
    try {
      target.performance.measure(PERFORMANCE_MEASURES.firstReadyView, {
        start: 0,
        end: PERFORMANCE_MARKS.activeViewReady,
        detail: { view },
      });
    } catch {
      // A browser without mark-name measure options still keeps the readiness mark.
    }
  }
  return marked;
}

export function markCharacterReady(characterId, target) {
  return markOnce(PERFORMANCE_MARKS.characterReady, { characterId }, target);
}

export function markMapReady(mapId, target) {
  return markOnce(PERFORMANCE_MARKS.mapReady, { mapId }, target);
}

export function markCombatBoardReady(encounterId, target) {
  return markOnce(PERFORMANCE_MARKS.combatBoardReady, { encounterId }, target);
}

export function resetPerformanceMarksForTests() {
  recordedMarks.clear();
}
