"use client";

import { useEffect, useRef, useState } from "react";

import type { HubContextSelection } from "../data/hub-types";

export function WorldCampaignSelector({
  busy,
  selection,
  onCampaignChange,
}: {
  busy: boolean;
  selection: HubContextSelection;
  onCampaignChange: (campaignId: string) => void;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [viewedWorldId, setViewedWorldId] = useState(selection.selectedWorldId);
  const selectedWorld = selection.worlds.find((world) => world.id === selection.selectedWorldId)
    ?? selection.worlds[0];
  const viewedWorld = selection.worlds.find((world) => world.id === viewedWorldId)
    ?? selectedWorld;
  const selectedCampaign = selectedWorld?.campaigns.find((campaign) =>
    campaign.id === selection.selectedCampaignId) ?? selectedWorld?.campaigns[0];

  useEffect(() => {
    setViewedWorldId(selection.selectedWorldId);
  }, [selection.selectedWorldId]);

  useEffect(() => {
    if (!open) return undefined;
    function onPointerDown(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
        rootRef.current?.querySelector<HTMLButtonElement>(".world-context__trigger")?.focus();
      }
    }
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  return (
    <div className="world-context" ref={rootRef}>
      <button
        aria-expanded={open}
        aria-haspopup="dialog"
        className="world-context__trigger"
        disabled={busy}
        onClick={() => setOpen((value) => !value)}
        type="button"
      >
        <span>
          <small>World</small>
          <strong>{selectedWorld?.name ?? "World"}</strong>
        </span>
        <i aria-hidden="true" />
        <span>
          <small>Campaign</small>
          <strong>{selectedCampaign?.name ?? "Campaign"}</strong>
        </span>
        <span aria-hidden="true" className="world-context__chevron">⌄</span>
      </button>

      {open ? (
        <div aria-label="Choose world and campaign" className="context-picker" role="dialog">
          <div className="context-picker__heading">
            <span>
              <small>Table context</small>
              <strong>Choose where to play</strong>
            </span>
            <button aria-label="Close world and campaign selection" onClick={() => setOpen(false)} type="button">×</button>
          </div>

          <div aria-label="Available worlds" className="context-picker__worlds">
            {selection.worlds.map((world) => (
              <button
                aria-pressed={world.id === viewedWorld?.id}
                className="context-picker__world"
                key={world.id}
                onClick={() => setViewedWorldId(world.id)}
                type="button"
              >
                <span>{world.name}</span>
                <small>{world.campaigns.length} {world.campaigns.length === 1 ? "campaign" : "campaigns"}</small>
              </button>
            ))}
          </div>

          <div aria-label={`Campaigns in ${viewedWorld?.name ?? "selected world"}`} className="context-picker__campaigns">
            <small>Campaigns in {viewedWorld?.name}</small>
            {viewedWorld?.campaigns.map((campaign) => {
              const current = campaign.id === selection.selectedCampaignId;
              return (
                <button
                  aria-current={current ? "true" : undefined}
                  className="context-picker__campaign"
                  disabled={busy}
                  key={campaign.id}
                  onClick={() => {
                    if (!current) onCampaignChange(campaign.id);
                    setOpen(false);
                  }}
                  type="button"
                >
                  <span>
                    <strong>{campaign.name}</strong>
                    <small>{current ? "Current campaign" : "Open campaign"}</small>
                  </span>
                  <b aria-hidden="true">{current ? "✓" : "→"}</b>
                </button>
              );
            })}
          </div>
        </div>
      ) : null}
    </div>
  );
}
