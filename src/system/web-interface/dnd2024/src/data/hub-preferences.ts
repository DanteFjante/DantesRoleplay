import type { HubEnvelope, Perspective } from "./hub-types";

export function requestedHubPreferences(storage: Pick<Storage, "getItem">) {
  try {
    const mode = storage.getItem("dnd2024-table-mode");
    const campaign = storage.getItem("dnd2024-table-campaign");
    return {
      perspective: (mode === "dm" ? "dm" : "player") as Perspective,
      campaignId: campaign && campaign.length <= 200 && campaign === campaign.trim() && !/\s/u.test(campaign)
        ? campaign : undefined,
    };
  } catch { return { perspective: "player" as Perspective, campaignId: undefined }; }
}

/** Preferences are requests; the server still chooses the authorized seat and campaign. */
export async function loadInitialHub(
  read: (perspective: Perspective, campaignId?: string) => Promise<HubEnvelope>,
  storage: Pick<Storage, "getItem">,
) {
  const requested = requestedHubPreferences(storage);
  const envelope = await read(requested.perspective, requested.campaignId);
  // An old campaign preference must not hide the host's current authorized campaign.
  return requested.campaignId && envelope.status === "denied"
    ? read(requested.perspective)
    : envelope;
}
