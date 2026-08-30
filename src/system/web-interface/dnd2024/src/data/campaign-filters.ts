import type { CampaignClue, CampaignQuest, CampaignThread } from "./hub-types";

function matches(query: string, values: string[]) {
  const normalized = query.trim().toLocaleLowerCase();
  return !normalized || values.some((value) => value.toLocaleLowerCase().includes(normalized));
}

export function filterCampaignQuests(
  quests: CampaignQuest[],
  { query = "", status = "all", kind = "all" } = {},
) {
  return [...quests]
    .filter((quest) =>
      (status === "all" || quest.status === status) &&
      (kind === "all" || quest.kind === kind) &&
      matches(query, [
        quest.kind, quest.status, quest.title, quest.summary, quest.nextStep,
        ...quest.objectives.flatMap((objective) => [objective.status, objective.text]),
        ...quest.links.locations.map((location) => location.name),
        ...quest.links.people.map((person) => person.name),
        ...quest.links.factions.map((faction) => faction.name),
      ]),
    )
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}

export function filterCampaignThreads(
  threads: CampaignThread[],
  { query = "", status = "all", category = "all" } = {},
) {
  return [...threads]
    .filter((thread) =>
      (status === "all" || thread.status === status) &&
      (category === "all" || thread.category === category) &&
      matches(query, [
        thread.category, thread.status, thread.pressure, thread.title, thread.summary, thread.lastChanged,
        ...thread.links.locations.map((location) => location.name),
        ...thread.links.people.map((person) => person.name),
        ...thread.links.factions.map((faction) => faction.name),
      ]),
    )
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}

export function filterCampaignClues(
  clues: CampaignClue[],
  { query = "", mystery = "all", status = "all" } = {},
) {
  return [...clues]
    .filter((clue) =>
      (mystery === "all" || clue.mystery === mystery) &&
      (status === "all" || clue.status === status) &&
      matches(query, [
        clue.mystery, clue.status, clue.title, clue.detail, clue.partyConclusion, clue.discoveredAt,
        ...clue.links.locations.map((location) => location.name),
        ...clue.links.people.map((person) => person.name),
        ...clue.links.factions.map((faction) => faction.name),
      ]),
    )
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}
