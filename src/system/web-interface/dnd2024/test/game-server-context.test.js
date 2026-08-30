import assert from "node:assert/strict";
import test from "node:test";

import {
  normalizeGameServerOrigin,
  readGameServerContext,
  resolvePresenceLocation,
} from "../src/server/game-server-context.js";

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function playerMapDirectoryResponse(path) {
  const id = path.includes("location.thalorien.brackenford")
    ? "location.thalorien.brackenford"
    : (path.includes("location.thalorien.crownmere") ? "location.thalorien.crownmere" : null);
  if (!id) return null;
  if (path.endsWith("/containment")) {
    return response(200, { containment: {
      containerEntityId: id.endsWith("brackenford") ? "location.thalorien.valeros" : "location.thalorien.aldros",
      slot: "location",
    } });
  }
  if (path.endsWith("/components/dnd2024.game.core.world.location")) {
    const isPublic = id.endsWith("brackenford");
    return response(200, {
      entityId: id,
      qualifiedTypeId: "dnd2024.game.core.world.location",
      valueJson: JSON.stringify({
        kind: "settlement",
        status: "draft",
        summary: isPublic ? "A frontier village." : "A hidden city.",
        visibility: isPublic ? "public" : "gm",
      }),
    });
  }
  if (path.endsWith("/components/dnd2024.game.core.world.map.anchor")) {
    return response(200, {
      entityId: id,
      qualifiedTypeId: "dnd2024.game.core.world.map.anchor",
      valueJson: JSON.stringify(id.endsWith("brackenford") ? { x: 232, y: 647 } : { x: 692, y: 516 }),
    });
  }
  if (path.endsWith("/components/dnd2024.game.core.world.map.visual")) {
    return response(200, {
      entityId: id,
      qualifiedTypeId: "dnd2024.game.core.world.map.visual",
      valueJson: JSON.stringify(id.endsWith("brackenford") ? {
        status: "active",
        variants: {
          player: { assetKey: "thalos.city.crownmere.player", alt: "Player-safe village map." },
          dm: { assetKey: "thalos.city.crownmere.dm", alt: "CANARY DM MAP." },
        },
      } : {
        status: "active",
        variants: { dm: { assetKey: "thalos.city.crownmere.dm", alt: "Hidden." } },
      }),
    });
  }
  return null;
}

test("normalizes only a credential-free HTTP(S) server origin", () => {
  assert.equal(normalizeGameServerOrigin("http://localhost:6217"), "http://localhost:6217");
  assert.equal(normalizeGameServerOrigin("https://table.example.test/"), "https://table.example.test");
  assert.equal(normalizeGameServerOrigin("http://user@example.test"), null);
  assert.equal(normalizeGameServerOrigin("http://example.test/api"), null);
  assert.equal(normalizeGameServerOrigin("file:///campaign"), null);
});

test("resolves only the ambient actor's exact authorized presence location", () => {
  const actorId = "actor.thalorien.brackenford.orban";
  const locations = ["location.thalorien.brackenford"];
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.brackenford",
    slot: "presence",
  } }, actorId, locations), "location.thalorien.brackenford");
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: "actor.thalorien.someone-else",
    containerEntityId: "location.thalorien.brackenford",
    slot: "presence",
  } }, actorId, locations), null);
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.brackenford",
    slot: "party",
  } }, actorId, locations), null);
  assert.equal(resolvePresenceLocation({ containment: {
    containedEntityId: actorId,
    containerEntityId: "location.thalorien.crownmere",
    slot: "presence",
  } }, actorId, locations), null);
});

test("reads the ambient actor's direct presence after the location is audience-authorized", async () => {
  const actorId = "actor.thalorien.brackenford.orban";
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      calls.push(path);
      if (path === "/api/audience-context") return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId: "campaign.thalorien.brackenford",
        actorId,
        role: "actor",
      });
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({ status: "active", premise: "A live campaign.", partyGoals: [], toneAndBoundaries: [] }),
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "Brackenford" });
      }
      if (path.endsWith(`/entities/${actorId}/containment`)) return response(200, { containment: {
        containedEntityId: actorId,
        containerEntityId: "location.thalorien.brackenford",
        slot: "presence",
      } });
      if (path.endsWith(`/entities/${actorId}`)) return response(200, { entityId: actorId, name: "Orban" });
      if (path.endsWith(`/entities/${actorId}/components/dnd2024.playtest-character-record`)) return response(404, {});
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "empty", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        return response(200, { items: [{ entityId: "location.thalorien.brackenford", name: "Brackenford" }] });
      }
      const locationResponse = playerMapDirectoryResponse(path);
      if (locationResponse) return locationResponse;
      return response(404, {});
    },
  });

  assert.equal(value.currentLocationId, "location.thalorien.brackenford");
  assert.equal(calls.filter((path) => path.endsWith(`/entities/${actorId}/containment`)).length, 1);
});

test("reads only the server-selected campaign, actor, and authorized knowledge", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({
            premise: "A waystone stirs beneath the woods.",
            partyGoals: ["Keep Brackenford safe."],
            toneAndBoundaries: ["Cozy high fantasy."],
          }),
        });
      }
      if (path.endsWith("/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record")) {
        return response(200, {
          entityId: "actor.thalorien.brackenford.orban",
          qualifiedTypeId: "dnd2024.playtest-character-record",
          valueJson: JSON.stringify({
            state: "active",
            entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
          }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, {
          status: "ready",
          entries: [{
            text: "Brackenford\nA frontier village beside the old forest.",
            stance: "known",
            presentationKind: "statement",
          }],
          locations: [{
            name: "Brackenford",
            entries: [{
              text: "Brackenford\nA frontier village beside the old forest.",
              stance: "known",
              presentationKind: "statement",
            }],
          }],
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/actor.thalorien.brackenford.orban")) {
        return response(200, { entityId: "actor.thalorien.brackenford.orban", name: "Orban" });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value, {
    version: 1,
    status: "connected",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    audience: { seat: "player", allowedPerspectives: ["player"] },
    contextSelection: {
      selectedWorldId: "world.thalorien",
      selectedCampaignId: "campaign.thalorien.brackenford",
      worlds: [{
        id: "world.thalorien",
        name: "Thalorien",
        campaigns: [{
          id: "campaign.thalorien.brackenford",
          name: "The Waystone at Brackenford",
        }],
      }],
    },
    campaign: {
      id: "campaign.thalorien.brackenford",
      name: "The Waystone at Brackenford",
      status: null,
      premise: "A waystone stirs beneath the woods.",
      partyGoals: ["Keep Brackenford safe."],
      toneAndBoundaries: ["Cozy high fantasy."],
        chapters: [],
        arcs: [],
        sessions: [],
    },
    actor: {
      id: "actor.thalorien.brackenford.orban",
      name: "Orban",
      state: "active",
      entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
    },
    party: [{
      id: "actor.thalorien.brackenford.orban",
      name: "Orban",
      current: true,
      state: "active",
      entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
    }],
    knowledge: {
      status: "ready",
      entries: [{
        text: "Brackenford\nA frontier village beside the old forest.",
        stance: "known",
        presentationKind: "statement",
      }],
      locations: [{
        name: "Brackenford",
        entries: [{
          text: "Brackenford\nA frontier village beside the old forest.",
          stance: "known",
          presentationKind: "statement",
        }],
      }],
    },
  });
  assert.deepEqual(calls, [
    "/api/audience-context",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/campaign.thalorien.brackenford",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record",
    "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
  ]);
});

test("maps a server-authorized game master context to the local DM seat without bound character-state reads", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          role: "game-master",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({ premise: "The DM knows the waystone's secret.", partyGoals: [], toneAndBoundaries: [] }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "empty", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        return response(200, {
          items: [
            { entityId: "location.thalorien.brackenford", name: "Brackenford" },
            { entityId: "actor.thalorien.brackenford.orban", name: "Orban" },
            { entityId: "location.thalorien.crownmere", name: "Crownmere" },
            { entityId: "faction.thalorien.gilded-concord", name: "The Gilded Concord" },
            { entityId: "faction.thalorien.archived", name: "Archived faction" },
          ],
        });
      }
      if (path.endsWith("/entities/faction.thalorien.gilded-concord/components/dnd2024.game.core.world.faction")) {
        return response(200, {
          entityId: "faction.thalorien.gilded-concord",
          qualifiedTypeId: "dnd2024.game.core.world.faction",
          valueJson: JSON.stringify({
            status: "active",
            visibility: "gm",
            summary: "A secret Merrowgate-centered merchant network.",
            goals: ["Weaken the Book of Truth's civic influence."],
            methods: ["Coordinate prices through private networks."],
            assets: ["Merchant houses and private lenders."],
            agenda: { state: "ready", summary: "Win broad royal support." },
          }),
        });
      }
      if (path.endsWith("/entities/faction.thalorien.archived/components/dnd2024.game.core.world.faction")) {
        return response(200, {
          entityId: "faction.thalorien.archived",
          qualifiedTypeId: "dnd2024.game.core.world.faction",
          valueJson: JSON.stringify({
            status: "archived",
            visibility: "gm",
            summary: "No longer active.",
            goals: ["Remain archived."],
            methods: ["None."],
            assets: [],
            agenda: { state: "ready", summary: "Do nothing." },
          }),
        });
      }
      if (path.endsWith("/relationships")) {
        return response(200, { items: [] });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value.audience, {
    seat: "dm",
    perspective: "dm",
    allowedPerspectives: ["dm", "player"],
  });
  assert.deepEqual(value.actor, { id: "local-game-master", name: "Dungeon Master", state: null, entries: [] });
  assert.equal(calls.some((path) => path.endsWith(
    "/entities/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record",
  )), false);
  assert.deepEqual(value.locationDirectory?.length, 2);
  assert.equal(value.locationDirectory[0].id, "location.thalorien.brackenford");
  assert.equal(value.locationDirectory[1].id, "location.thalorien.crownmere");
  assert.equal(value.worldDirectory?.factions[0].id, "faction.thalorien.gilded-concord");
  assert.equal(value.worldDirectory?.factions.length, 1);
  assert.deepEqual(value.worldDirectory?.factions[0].assets, ["Merchant houses and private lenders."]);
});

test("reads location directory items that use `id` instead of `entityId`", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          role: "game-master",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({ premise: "The DM knows the waystone's secret.", partyGoals: [], toneAndBoundaries: [] }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "empty", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        return response(200, {
          items: [
            { id: "location.thalorien.brackenford", name: "Brackenford" },
            { id: "location.thalorien.crownmere", name: "Crownmere" },
          ],
        });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/containment")) {
        return response(200, {
          containment: {
            containedEntityId: "location.thalorien.brackenford",
            containerEntityId: "location.thalorien.valeros",
          },
        });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/components/dnd2024.game.core.world.location")) {
        return response(200, {
          entityId: "location.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.world.location",
          valueJson: JSON.stringify({ summary: "A frontier village." }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/containment")) {
        return response(404, {});
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/components/dnd2024.game.core.world.location")) {
        return response(200, {
          entityId: "location.thalorien.crownmere",
          qualifiedTypeId: "dnd2024.game.core.world.location",
          valueJson: JSON.stringify({ summary: "A quiet port town." }),
        });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value.locationDirectory?.length, 2);
  assert.equal(value.locationDirectory[0].id, "location.thalorien.brackenford");
  assert.equal(value.locationDirectory[1].id, "location.thalorien.crownmere");
  assert.equal(value.locationDirectory[0].name, "Brackenford");
  assert.equal(value.locationDirectory[1].name, "Crownmere");
  assert.equal(value.locationDirectory[0].summary, "A frontier village.");
  assert.equal(value.locationDirectory[1].summary, "A quiet port town.");
  assert.equal(value.locationDirectory[0].containerId, "location.thalorien.valeros");
  assert.equal("containerId" in value.locationDirectory[1], false);
});

test("keeps partial location-directory pages when a later page fetch fails", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      const cursor = requested.searchParams.get("cursor");

      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          role: "game-master",
        });
      }

      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({
            premise: "The DM knows the waystone's secret.",
            partyGoals: [],
            toneAndBoundaries: [],
          }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "ready", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        if (cursor === null) {
          return response(200, {
            items: [{ entityId: "location.thalorien.aldros", name: "Aldros" }],
            nextCursor: "page2",
          });
        }
        return response(503, { status: "UNAVAILABLE" });
      }
      if (path.endsWith("/entities/location.thalorien.aldros")) {
        return response(200, { entityId: "location.thalorien.aldros", name: "Aldros" });
      }
      if (path.endsWith("/entities/location.thalorien.aldros/components/dnd2024.game.core.world.location")) {
        return response(503, { status: "UNAVAILABLE" });
      }

      return response(404, {});
    },
  });

  assert.deepEqual(value.audience, {
    seat: "dm",
    perspective: "dm",
    allowedPerspectives: ["dm", "player"],
  });
  assert.deepEqual(value.locationDirectory, [{ id: "location.thalorien.aldros", name: "Aldros" }]);
  assert.equal(value.locationDirectory.length, 1);
});

test("maps a server-authorized actor binding to local DM seat when localSeat overrides it", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    localSeat: "dm",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
          roleHints: {},
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({ premise: "The DM knows the waystone's secret.", partyGoals: [], toneAndBoundaries: [] }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "ready", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        return response(200, {
          items: [
            { entityId: "location.thalorien.brackenford", name: "Brackenford" },
            { entityId: "actor.thalorien.brackenford.orban", name: "Orban" },
            { entityId: "location.thalorien.crownmere", name: "Crownmere" },
          ],
        });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/containment")) {
        return response(200, { containment: { containerEntityId: "location.thalorien.valeros", slot: "location" } });
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/containment")) {
        return response(200, { containment: { containerEntityId: "location.thalorien.aldros", slot: "location" } });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/components/dnd2024.game.core.world.location")) {
        return response(200, {
          entityId: "location.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.world.location",
          valueJson: JSON.stringify({ kind: "settlement", status: "draft", summary: "A frontier village.", visibility: "public" }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/components/dnd2024.game.core.world.location")) {
        return response(200, {
          entityId: "location.thalorien.crownmere",
          qualifiedTypeId: "dnd2024.game.core.world.location",
          valueJson: JSON.stringify({ kind: "settlement", status: "draft", summary: "A hidden city.", visibility: "gm" }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/components/dnd2024.game.core.world.map.anchor")) {
        return response(200, {
          entityId: "location.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.world.map.anchor",
          valueJson: JSON.stringify({ x: 232, y: 647 }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/components/dnd2024.game.core.world.map.anchor")) {
        return response(200, {
          entityId: "location.thalorien.crownmere",
          qualifiedTypeId: "dnd2024.game.core.world.map.anchor",
          valueJson: JSON.stringify({ x: 692, y: 516 }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.brackenford/components/dnd2024.game.core.world.map.visual")) {
        return response(200, {
          entityId: "location.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.world.map.visual",
          valueJson: JSON.stringify({
            status: "active",
            variants: {
              player: { assetKey: "thalos.city.crownmere.player", alt: "Player-safe village map." },
              dm: { assetKey: "thalos.city.crownmere.dm", alt: "CANARY DM MAP." },
            },
          }),
        });
      }
      if (path.endsWith("/entities/location.thalorien.crownmere/components/dnd2024.game.core.world.map.visual")) {
        return response(200, {
          entityId: "location.thalorien.crownmere",
          qualifiedTypeId: "dnd2024.game.core.world.map.visual",
          valueJson: JSON.stringify({ status: "active", variants: { dm: { assetKey: "thalos.city.crownmere.dm", alt: "Hidden." } } }),
        });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value.audience, {
    seat: "dm",
    perspective: "dm",
    allowedPerspectives: ["dm", "player"],
  });
  assert.deepEqual(value.actor, { id: "local-game-master", name: "Dungeon Master", state: null, entries: [] });
  assert.equal(calls.some((path) => path.endsWith(
    "/entities/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record",
  )), false);
});

test("supports selecting player perspective for a local game master", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    requestedPerspective: "player",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          role: "game-master",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" });
      }
      if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
        return response(200, {
          entityId: "campaign.thalorien.brackenford",
          qualifiedTypeId: "dnd2024.game.core.campaign.root",
          valueJson: JSON.stringify({ premise: "The DM knows the waystone's secret.", partyGoals: [], toneAndBoundaries: [] }),
        });
      }
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "empty", entries: [], locations: [] });
      }
      if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
        return response(200, {
          items: [
            { entityId: "location.thalorien.brackenford", name: "Brackenford" },
            { entityId: "actor.thalorien.brackenford.orban", name: "Orban" },
            { entityId: "location.thalorien.crownmere", name: "Crownmere" },
          ],
        });
      }
      const mapResponse = playerMapDirectoryResponse(path);
      if (mapResponse) return mapResponse;
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value.audience, {
    seat: "dm",
    perspective: "player",
    allowedPerspectives: ["dm", "player"],
  });
  assert.equal(value.locationDirectoryAudience, "player");
  assert.deepEqual(value.locationDirectory, [{
    id: "location.thalorien.brackenford",
    name: "Brackenford",
    kind: "settlement",
    summary: "A frontier village.",
    containerId: "location.thalorien.valeros",
    containmentSlot: "location",
    mapAnchor: { x: 232, y: 647 },
    mapVisual: { assetKey: "thalos.city.crownmere.player", alt: "Player-safe village map." },
  }]);
  assert.equal(JSON.stringify(value).includes("CANARY DM MAP"), false);
  assert.equal(JSON.stringify(value).includes("A hidden city"), false);
  assert.deepEqual(value.party, []);
  assert.deepEqual(value.knowledge, { status: "unavailable", entries: [], locations: [] });
  assert.equal(calls.some((path) => path.endsWith("/campaign.thalorien.brackenford/knowledge")), false);
  assert.equal(calls.some((path) => path.includes("has-character-participation")), false);
  assert.equal(calls.length, 14);
});

test("reads live campaign chapters and arcs while omitting GM context from player preview", async () => {
  const fetchImpl = async (input) => {
    const requested = new URL(input);
    const path = requested.pathname;
    if (path === "/api/audience-context") {
      return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId: "campaign.thalorien.brackenford",
        role: "game-master",
      });
    }
    if (path.endsWith("/campaign.thalorien.brackenford")) {
      return response(200, {
        entityId: "campaign.thalorien.brackenford",
        name: "The Waystone at Brackenford",
      });
    }
    if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root")) {
      return response(200, {
        entityId: "campaign.thalorien.brackenford",
        qualifiedTypeId: "dnd2024.game.core.campaign.root",
        valueJson: JSON.stringify({
          status: "active",
          premise: "A waystone stirs beneath the woods.",
          partyGoals: ["Keep Brackenford safe."],
          toneAndBoundaries: [],
        }),
      });
    }
    if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
      return response(200, { status: "empty", entries: [], locations: [] });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
      return response(200, {
        items: [
          {
            entityId: "campaign.thalorien.brackenford.chapter.arrivals",
            name: "Brackenford Arrivals",
            createdAtUtc: "2026-08-22T21:04:50Z",
          },
          {
            entityId: "campaign.thalorien.brackenford.arc.waking-depths",
            name: "The Waking Depths",
            createdAtUtc: "2026-08-22T21:05:00Z",
          },
        ],
      });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/relationships" &&
        requested.searchParams.get("qualifiedKind") === "dnd2024.game.core.campaign.has-session") {
      return response(200, {
        items: [{
          fromEntityId: "campaign.thalorien.brackenford",
          toEntityId: "session.thalorien.brackenford.1",
          qualifiedKind: "dnd2024.game.core.campaign.has-session",
        }],
      });
    }
    if (path.endsWith("/session.thalorien.brackenford.1/components/dnd2024.game.core.campaign.session")) {
      return response(200, {
        entityId: "session.thalorien.brackenford.1",
        qualifiedTypeId: "dnd2024.game.core.campaign.session",
        valueJson: JSON.stringify({ status: "ended", ordinal: 1 }),
        updatedAtUtc: "2026-08-23T10:02:00Z",
      });
    }
    if (path.endsWith("/session.thalorien.brackenford.1/components/dnd2024.game.core.campaign.session-recap")) {
      return response(200, {
        entityId: "session.thalorien.brackenford.1",
        qualifiedTypeId: "dnd2024.game.core.campaign.session-recap",
        valueJson: JSON.stringify({
          protocolVersion: "session.s0.c3-only.v1",
          chapter: {
            id: "campaign.thalorien.brackenford.chapter.arrivals",
            status: "active",
            title: "Brackenford Arrivals",
            partyQuestion: "Why have the goblins stopped raiding?",
          },
          arc: {
            id: "campaign.thalorien.brackenford.arc.waking-depths",
            status: "active",
            title: "The Waking Depths",
            partyStake: "Brackenford's peace depends on finding the truth.",
          },
          milestones: [],
        }),
      });
    }
    if (path.endsWith("/campaign.thalorien.brackenford.chapter.arrivals/components/dnd2024.game.core.campaign.chapter")) {
      return response(200, {
        entityId: "campaign.thalorien.brackenford.chapter.arrivals",
        qualifiedTypeId: "dnd2024.game.core.campaign.chapter",
        valueJson: JSON.stringify({
          status: "active",
          title: "Brackenford Arrivals",
          partyQuestion: "Why have the goblins stopped raiding?",
          gmContext: "The waystone is waking.",
        }),
        updatedAtUtc: "2026-08-23T10:00:00Z",
      });
    }
    if (path.endsWith("/campaign.thalorien.brackenford.arc.waking-depths/components/dnd2024.game.core.campaign.arc")) {
      return response(200, {
        entityId: "campaign.thalorien.brackenford.arc.waking-depths",
        qualifiedTypeId: "dnd2024.game.core.campaign.arc",
        valueJson: JSON.stringify({
          status: "active",
          title: "The Waking Depths",
          partyStake: "Brackenford's peace depends on finding the truth.",
          gmContext: "The danger lies below the old cellar.",
        }),
        updatedAtUtc: "2026-08-23T10:01:00Z",
      });
    }
    throw new Error(`Unexpected request ${path}`);
  };

  const dm = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl,
    requestedPerspective: "dm",
  });
  const playerPreview = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl,
    requestedPerspective: "player",
  });

  assert.equal(dm.campaign.status, "active");
  assert.equal(dm.campaign.chapters[0].title, "Brackenford Arrivals");
  assert.equal(dm.campaign.chapters[0].gmContext, "The waystone is waking.");
  assert.equal(dm.campaign.arcs[0].title, "The Waking Depths");
  assert.equal(dm.campaign.arcs[0].gmContext, "The danger lies below the old cellar.");
  assert.equal(dm.campaign.sessions[0].ordinal, 1);
  assert.equal(dm.campaign.sessions[0].recap.chapter.title, "Brackenford Arrivals");
  assert.equal("gmContext" in playerPreview.campaign.chapters[0], false);
  assert.equal("gmContext" in playerPreview.campaign.arcs[0], false);
  assert.deepEqual(playerPreview.campaign.sessions, []);
});

test("fails closed when the server knowledge projection is unavailable", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "Brackenford" });
      }
      if (path.endsWith("/actor.thalorien.brackenford.orban")) {
        return response(200, { entityId: "actor.thalorien.brackenford.orban", name: "Orban" });
      }
      if (path.includes("/knowledge")) return response(503, { error: "KNOWLEDGE_UNAVAILABLE" });
      return response(404, {});
    },
  });

  assert.deepEqual(value.knowledge, { status: "unavailable", entries: [], locations: [] });
});

test("does not expose malformed server knowledge entries", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "Brackenford" });
      }
      if (path.endsWith("/actor.thalorien.brackenford.orban")) {
        return response(200, { entityId: "actor.thalorien.brackenford.orban", name: "Orban" });
      }
      if (path.includes("/knowledge")) {
        return response(200, {
          status: "ready",
          entries: [{ text: "This field must not reach the UI", stance: "known" }],
        });
      }
      return response(404, {});
    },
  });

  assert.deepEqual(value.knowledge, { status: "unavailable", entries: [], locations: [] });
});

test("fails closed for a malformed known-location entry", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
        });
      }
      if (path.endsWith("/campaign.thalorien.brackenford")) {
        return response(200, { entityId: "campaign.thalorien.brackenford", name: "Brackenford" });
      }
      if (path.endsWith("/actor.thalorien.brackenford.orban")) {
        return response(200, { entityId: "actor.thalorien.brackenford.orban", name: "Orban" });
      }
      if (path.includes("/knowledge")) {
        return response(200, {
          status: "ready",
          entries: [{ text: "A valid notebook entry.", stance: "known", presentationKind: "statement" }],
          locations: [{ name: "Brackenford", entries: [{ text: "Leaked", stance: "known" }] }],
        });
      }
      return response(404, {});
    },
  });

  assert.deepEqual(value.knowledge, { status: "unavailable", entries: [], locations: [] });
});

test("does not read campaign state after an audience denial", async () => {
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl: async () => response(403, { status: "denied", error: "AUDIENCE_CONTEXT_DENIED" }),
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "The game server did not authorize a campaign for this local table.",
  });
});

test("groups readable campaign roots by World and switches a local DM to an exact choice", async () => {
  const calls = [];
  const fetchImpl = async (input) => {
    const requested = new URL(input);
    const path = requested.pathname;
    calls.push(path);
    if (path === "/api/audience-context") {
      return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId: "campaign.thalorien.brackenford",
        role: "game-master",
      });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
      return response(200, {
        items: [
          { entityId: "world.thalorien", name: "Thalorien" },
          { entityId: "world.embersea", name: "The Ember Sea" },
          { entityId: "campaign.thalorien.brackenford", name: "The Waystone at Brackenford" },
          { entityId: "campaign.thalorien.second-age", name: "The Second Age" },
          { entityId: "campaign.embersea.black-tide", name: "The Black Tide" },
          { entityId: "campaign.embersea.black-tide.chapter.opening", name: "Opening" },
        ],
      });
    }
    if (path.endsWith("/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root") ||
        path.endsWith("/campaign.thalorien.second-age/components/dnd2024.game.core.campaign.root") ||
        path.endsWith("/campaign.embersea.black-tide/components/dnd2024.game.core.campaign.root")) {
      const entityId = decodeURIComponent(path.split("/components/")[0].split("/").at(-1));
      return response(200, {
        entityId,
        qualifiedTypeId: "dnd2024.game.core.campaign.root",
        valueJson: JSON.stringify({ status: "active", premise: "A readable campaign.", partyGoals: [], toneAndBoundaries: [] }),
      });
    }
    if (path.endsWith("/campaign.embersea.black-tide.chapter.opening/components/dnd2024.game.core.campaign.root")) {
      return response(404, {});
    }
    if (path.endsWith("/campaign.embersea.black-tide")) {
      return response(200, { entityId: "campaign.embersea.black-tide", name: "The Black Tide" });
    }
    if (path === "/api/applications/dnd2024/campaigns/campaign.embersea.black-tide/knowledge") {
      return response(200, { status: "empty", entries: [], locations: [] });
    }
    return response(404, {});
  };

  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl,
    requestedCampaignId: "campaign.embersea.black-tide",
  });

  assert.equal(value.campaign.id, "campaign.embersea.black-tide");
  assert.equal(value.contextSelection.selectedWorldId, "world.embersea");
  assert.equal(value.contextSelection.selectedCampaignId, "campaign.embersea.black-tide");
  assert.deepEqual(value.contextSelection.worlds.map((world) => [
    world.name,
    world.campaigns.map((campaign) => campaign.name),
  ]), [
    ["Thalorien", ["The Second Age", "The Waystone at Brackenford"]],
    ["The Ember Sea", ["The Black Tide"]],
  ]);
  assert.equal(calls.some((path) => path.includes("chapter.opening/components/dnd2024.game.core.campaign.root")), true);
});

test("rejects an actor's cross-campaign request before reading campaign detail", async () => {
  const calls = [];
  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    requestedCampaignId: "campaign.embersea.black-tide",
    fetchImpl: async (input) => {
      const path = new URL(input).pathname;
      calls.push(path);
      if (path === "/api/audience-context") {
        return response(200, {
          status: "bound",
          applicationId: "dnd2024",
          stateSpaceId: "dnd2024-main",
          campaignId: "campaign.thalorien.brackenford",
          actorId: "actor.thalorien.brackenford.orban",
          role: "actor",
        });
      }
      throw new Error(`Unexpected request ${path}`);
    },
  });

  assert.deepEqual(value, {
    version: 1,
    status: "denied",
    message: "That campaign is not available to this local table.",
  });
  assert.deepEqual(calls, ["/api/audience-context"]);
});

test("projects only exact active campaign participation into the DM party roster", async () => {
  const campaignId = "campaign.thalorien.brackenford";
  const activeParticipation = `${campaignId}.participation.actor.thalorien.brackenford.orban`;
  const withdrawnParticipation = `${campaignId}.participation.actor.thalorien.brackenford.sol`;
  const fetchImpl = async (input) => {
    const requested = new URL(input);
    const path = requested.pathname;
    const fromEntityId = requested.searchParams.get("fromEntityId");
    const qualifiedKind = requested.searchParams.get("qualifiedKind");
    if (path === "/api/audience-context") {
      return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId,
        role: "game-master",
      });
    }
    if (path.endsWith(`/${campaignId}/components/dnd2024.game.core.campaign.root`)) {
      return response(200, {
        entityId: campaignId,
        qualifiedTypeId: "dnd2024.game.core.campaign.root",
        valueJson: JSON.stringify({ status: "active", premise: "A live campaign.", partyGoals: [], toneAndBoundaries: [] }),
      });
    }
    if (path.endsWith(`/${campaignId}`)) {
      return response(200, { entityId: campaignId, name: "The Waystone at Brackenford" });
    }
    if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
      return response(200, { status: "empty", entries: [], locations: [] });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/entities") {
      return response(200, { items: [
        { entityId: campaignId, name: "The Waystone at Brackenford" },
        { entityId: "actor.thalorien.brackenford.orban", name: "Orban" },
        { entityId: "actor.thalorien.brackenford.sol", name: "Sol" },
      ], nextCursor: null });
    }
    if (path.endsWith("/relationships") &&
        fromEntityId === campaignId &&
        qualifiedKind === "dnd2024.game.core.campaign.has-character-participation") {
      return response(200, { items: [
        { fromEntityId: campaignId, toEntityId: withdrawnParticipation, qualifiedKind },
        { fromEntityId: campaignId, toEntityId: activeParticipation, qualifiedKind },
      ] });
    }
    if (path.endsWith("/relationships") &&
        qualifiedKind === "dnd2024.game.core.campaign.character-participation.for-actor") {
      const actorId = fromEntityId === activeParticipation
        ? "actor.thalorien.brackenford.orban"
        : "actor.thalorien.brackenford.sol";
      return response(200, { items: [{ fromEntityId, toEntityId: actorId, qualifiedKind }] });
    }
    if (path.endsWith("/relationships") &&
        qualifiedKind === "dnd2024.game.core.campaign.has-session") {
      return response(200, { items: [] });
    }
    if (path.endsWith(`/${activeParticipation}/components/dnd2024.game.core.campaign.character-participation`)) {
      return response(200, {
        entityId: activeParticipation,
        qualifiedTypeId: "dnd2024.game.core.campaign.character-participation",
        valueJson: JSON.stringify({ status: "active" }),
      });
    }
    if (path.endsWith(`/${withdrawnParticipation}/components/dnd2024.game.core.campaign.character-participation`)) {
      return response(200, {
        entityId: withdrawnParticipation,
        qualifiedTypeId: "dnd2024.game.core.campaign.character-participation",
        valueJson: JSON.stringify({ status: "withdrawn" }),
      });
    }
    if (path.endsWith("/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record")) {
      return response(200, {
        entityId: "actor.thalorien.brackenford.orban",
        qualifiedTypeId: "dnd2024.playtest-character-record",
        valueJson: JSON.stringify({
          state: "active",
          entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
        }),
      });
    }
    if (path.endsWith("/actor.thalorien.brackenford.orban")) {
      return response(200, { entityId: "actor.thalorien.brackenford.orban", name: "Orban" });
    }
    if (path.endsWith("/actor.thalorien.brackenford.sol/components/dnd2024.playtest-character-record")) {
      return response(200, {
        entityId: "actor.thalorien.brackenford.sol",
        qualifiedTypeId: "dnd2024.playtest-character-record",
        valueJson: JSON.stringify({ state: "active", entries: [] }),
      });
    }
    if (path.endsWith("/actor.thalorien.brackenford.sol")) {
      return response(200, { entityId: "actor.thalorien.brackenford.sol", name: "Sol" });
    }
    return response(404, {});
  };

  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    fetchImpl,
    requestedPerspective: "dm",
  });

  assert.deepEqual(value.party, [{
    id: "actor.thalorien.brackenford.orban",
    name: "Orban",
    state: "active",
    entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
    current: false,
  }]);
  assert.equal(JSON.stringify(value).includes("actor.thalorien.brackenford.sol"), false);
});
