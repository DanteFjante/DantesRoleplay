import type {
  ConnectedCampaignEnvelope,
  WorldHistoryEvent,
  WorldLoreEntry,
} from "./hub-types";

type KnowledgeEntry = ConnectedCampaignEnvelope["knowledge"]["entries"][number];

type HistoricalPresentation = Pick<
  WorldHistoryEvent,
  "sortOrder" | "date" | "era" | "category" | "region"
>;

// Presentation classifications for already-authorized records. This does not create or amend
// world facts. Only time-bound turning points belong here; enduring places, customs, institutions,
// peoples, and current conditions remain lore even when their descriptions mention the past.
const THALORIEN_HISTORY = {
  "The Veiled Age": event(100, "Before the current age", "The Veiled Age", "Era"),
  "The Forgotten Dungeons Grow Dangerous": event(120, "Across more than four millennia", "The Layered Past", "Change"),
  "The Origin of the Great Monster Invasion": event(200, "Around 2,000 years ago", "The Monster Invasion", "Calamity"),
  "The Monster Outbreak": event(210, "Around 2,000 years ago", "The Monster Invasion", "Calamity"),
  "The Sudden Calamity": event(220, "Around 2,000 years ago", "The Monster Invasion", "Calamity"),
  "The Near Extinction": event(230, "Around 2,000 years ago", "The Monster Invasion", "Calamity"),
  "The First Systematic Monster Defence": event(240, "Around 2,000 years ago", "The Monster Invasion", "Defence"),
  "Thalmon's Monster Hunt": event(250, "Around 2,000 years ago", "The Monster Invasion", "Defence"),
  "The Cave-Sealing Pattern": event(260, "Around 2,000 years ago", "The Monster Invasion", "Defence"),
  "The Underground Monster Caves": event(270, "Around 2,000 years ago", "The Monster Invasion", "Discovery"),
  "The Thalmos Foundation": event(300, "Around 2,000 years ago", "The Imperial Founding", "Founding"),
  "The Eight-Sibling Council": event(310, "Early current age", "The Imperial Succession", "Politics"),
  "The Siblings' Incompatible Priorities": event(320, "Early current age", "The Imperial Succession", "Conflict"),
  "The Death of Oberon": event(330, "Early current age", "The Imperial Succession", "Succession"),
  "The Rebellion Against the Council": event(340, "Early current age", "The Imperial Succession", "Rebellion"),
  "The Seven-Way Division": event(350, "Early current age", "The Imperial Succession", "Succession"),
  "The Founding Allocation of Kingdoms": event(360, "Early current age", "The Seven Kingdoms", "Founding"),
  "Harrowfall Surpassed Merrowgate": event(400, "Before the Great Thalos War", "The Rival Cities", "Trade", "Evandos"),
  "The Origin of the Dark Market": event(410, "Before the Great Thalos War", "The Rival Cities", "Trade"),
  "The Hidden Knowledge Networks": event(420, "Before the Great Thalos War", "The Rival Cities", "Knowledge"),
  "The King of Merceros Takes Offence": event(430, "Before the Great Thalos War", "The Rival Cities", "Politics", "Merceros"),
  "The Great Seven-Kingdom War": event(500, "About 1,530 years ago", "The Great Thalos War", "War"),
  "The Merceros-Valeros War Alliance": event(510, "About 1,530 years ago", "The Great Thalos War", "War"),
  "The Destruction of Harrowfall": event(520, "About 1,530 years ago", "The Great Thalos War", "Destruction", "Evandos"),
  "The Harrowfall Massacre": event(530, "About 1,530 years ago", "The Great Thalos War", "Atrocity", "Evandos"),
  "The Destruction of Valeros's Magic Tower": event(540, "About 1,530 years ago", "The Great Thalos War", "Destruction", "Valeros"),
  "The Destruction of the Arts School": event(550, "About 1,530 years ago", "The Great Thalos War", "Destruction"),
  "The Hunted Tower Magicians": event(560, "About 1,530 years ago", "The Great Thalos War", "Persecution"),
  "The Scattered Battle Magicians": event(570, "About 1,530 years ago", "The Great Thalos War", "Aftermath"),
  "The Towers' Battle-Magic Role": event(580, "About 1,530 years ago", "The Great Thalos War", "War"),
  "The Great Thalos War Ended 1,530 Years Ago": event(600, "1,530 years ago", "The Great Thalos War", "Peace"),
  "Postwar Losses and Humility": event(610, "After the Great Thalos War", "The Long Peace", "Aftermath"),
  "The Traveling Troupes": event(620, "After the Great Thalos War", "The Long Peace", "Culture"),
  "Elven Remembrance of Thalmon's Ideals": event(630, "Across the Long Peace", "The Long Peace", "Memory"),
  "Council Stagnation": event(700, "Present age", "The Long Peace", "Politics"),
} satisfies Record<string, HistoricalPresentation>;

function event(
  sortOrder: number,
  date: string,
  era: string,
  category: string,
  region = "Thalos",
): HistoricalPresentation {
  return { sortOrder, date, era, category, region };
}

function normalizedHistoricalTitle(value: string): string {
  return value
    .replace(/[\u2010-\u2015\u2212\ufffd]/gu, "-")
    .replace(/\s+/gu, " ")
    .trim();
}

function splitAuthorizedText(text: string, fallbackNumber: number) {
  const lines = text.split(/\r?\n/u).map((line) => line.trim()).filter(Boolean);
  if (lines.length <= 1) {
    const body = lines[0] ?? text.trim();
    return { title: `Known information ${fallbackNumber}`, body, summary: body };
  }

  const title = lines[0];
  const bodyLines = lines.length >= 3 ? lines.slice(1, -1) : lines.slice(1);
  const body = bodyLines.join("\n\n").trim() || title;
  const sentence = body.match(/^.*?[.!?](?:\s|$)/u)?.[0]?.trim();
  return { title, body, summary: sentence || body };
}

function remainingConsequence(body: string, summary: string): string {
  const remainder = body.slice(summary.length).trim();
  return remainder || "No separate persistent consequence is recorded in this entry.";
}

export function classifyThalorienKnowledge(entries: KnowledgeEntry[]): {
  history: WorldHistoryEvent[];
  lore: WorldLoreEntry[];
} {
  const history: WorldHistoryEvent[] = [];
  const lore: WorldLoreEntry[] = [];

  entries.forEach((entry, index) => {
    const parsed = splitAuthorizedText(entry.text, index + 1);
    const presentation = THALORIEN_HISTORY[
      normalizedHistoricalTitle(parsed.title) as keyof typeof THALORIEN_HISTORY
    ];

    if (presentation) {
      history.push({
        id: `live-history-${index}`,
        ...presentation,
        title: parsed.title,
        status: entry.stance,
        summary: parsed.summary,
        consequence: remainingConsequence(parsed.body, parsed.summary),
        linkedLocations: [],
        linkedPeople: [],
      });
      return;
    }

    lore.push({
      id: `live-lore-${index}`,
      title: parsed.title,
      category: entry.presentationKind === "statement" ? "World lore" : entry.presentationKind,
      status: entry.stance,
      summary: parsed.summary,
      body: parsed.body,
      linkedLocations: [],
      linkedPeople: [],
      linkedFactions: [],
      linkedHistory: [],
    });
  });

  return { history, lore };
}
