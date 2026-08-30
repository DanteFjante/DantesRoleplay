import assert from "node:assert/strict";
import test from "node:test";

import {
  normalizeGameServerOrigin,
  projectMediaVisual,
  readCombatCurrentScene,
  readConversationCurrentScene,
  readGameServerContext,
  readKnownOpenRoutes,
  resolveCurrentSceneRecord,
  resolveSceneAffordancesRecord,
  resolvePresenceLocation,
} from "../src/server/game-server-context.js";

const MEDIA_HASH = "3ae0336e89155a4a00fb0d982ae903bf9ed1137cd292b097b252fd38c1501fa3";

function mediaRecord(variants) {
  return {
    status: "active",
    slots: {
      portrait: {
        variants,
        provenance: {
          kind: "generated",
          credit: "Reviewed original artwork",
          source: "reviewed/portrait.png",
          reviewedOn: "2026-08-30",
          version: 1,
        },
      },
    },
  };
}

function mediaVariant(alt = "A reviewed portrait") {
  return {
    assetKey: `sha256.${MEDIA_HASH}`,
    alt,
    mimeType: "image/png",
    width: 1024,
    height: 1536,
    sha256: MEDIA_HASH,
  };
}

test("visual media selects only the exact audience variant and returns no private registry metadata", () => {
  const record = mediaRecord({
    player: mediaVariant("Player portrait"),
    dm: mediaVariant("DM portrait"),
  });
  assert.deepEqual(projectMediaVisual(record, "player", "/ui/dnd2024-play/assets/"), {
    portrait: {
      imageUrl: `/components/media/sha256.${MEDIA_HASH}.png`,
      alt: "Player portrait",
      width: 1024,
      height: 1536,
    },
  });
  const serialized = JSON.stringify(projectMediaVisual(record, "player"));
  assert.equal(serialized.includes("DM portrait"), false);
  assert.equal(serialized.includes("assetKey"), false);
  assert.equal(serialized.includes("sha256"), true, "content-addressed URL is the only emitted digest use");
  assert.equal(serialized.includes("provenance"), false);
});

test("visual media omits missing audience variants and fails closed on malformed or mismatched assets", () => {
  assert.equal(projectMediaVisual(mediaRecord({ dm: mediaVariant("DM only") }), "player"), null);
  assert.equal(projectMediaVisual({ ...mediaRecord({ player: mediaVariant() }), injected: true }, "player"), null);
  assert.equal(projectMediaVisual(mediaRecord({
    player: { ...mediaVariant(), assetKey: "sha256.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
  }), "player"), null);
  assert.equal(projectMediaVisual({ ...mediaRecord({ player: mediaVariant() }), status: "archived" }, "player"), null);
});

test("current scene records resolve encounter then conversation then exploration", () => {
  const locations = ["location.thalorien.brackenford"];
  assert.deepEqual(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley" },
    encounter: { entityId: "encounter.brackenford.ambush" },
  }, locations), {
    kind: "combat",
    locationId: locations[0],
    conversationId: "interaction.brackenford.parley",
    encounterId: "encounter.brackenford.ambush",
  });
  assert.deepEqual(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley" },
  }, locations), {
    kind: "conversation",
    locationId: locations[0],
    conversationId: "interaction.brackenford.parley",
  });
  assert.deepEqual(resolveCurrentSceneRecord({ location: { entityId: locations[0] } }, locations), {
    kind: "exploration",
    locationId: locations[0],
  });
});

test("current scene records reject unknown locations and open or malformed references", () => {
  const locations = ["location.thalorien.brackenford"];
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: "location.thalorien.hidden" },
  }, locations), null);
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    conversation: { entityId: "interaction.brackenford.parley", name: "Injected" },
  }, locations), null);
  assert.equal(resolveCurrentSceneRecord({
    location: { entityId: locations[0] },
    guessedMode: "combat",
  }, locations), null);
});

test("scene affordances match the full current scene and filter GM-only context", () => {
  const currentScene = {
    kind: "combat",
    locationId: "location.thalorien.brackenford",
    conversationId: "interaction.brackenford.parley",
    encounterId: "encounter.brackenford.ambush",
  };
  const record = {
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
      encounter: { entityId: currentScene.encounterId },
    },
    items: [
      { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall.", visibility: "party" },
      { key: "spring-ambush", label: "Spring the ambush", summary: "Reveal the hidden archers.", visibility: "gm" },
    ],
  };

  assert.deepEqual(resolveSceneAffordancesRecord(record, currentScene, "player"), [
    { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall." },
  ]);
  assert.deepEqual(resolveSceneAffordancesRecord(record, currentScene, "dm"), [
    { key: "take-cover", label: "Take cover", summary: "Move behind the ruined wall." },
    { key: "spring-ambush", label: "Spring the ambush", summary: "Reveal the hidden archers." },
  ]);
});

test("scene affordances fail closed for stale selectors and duplicate keys", () => {
  const currentScene = {
    kind: "conversation",
    locationId: "location.thalorien.brackenford",
    conversationId: "interaction.brackenford.parley",
  };
  const item = { key: "ask-about-road", label: "Ask about the road", summary: "Learn what lies ahead.", visibility: "party" };
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: "interaction.brackenford.stale" },
    },
    items: [item],
  }, currentScene, "player"), null);
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
    },
    items: [item, { ...item, label: "Duplicate" }],
  }, currentScene, "player"), null);
  assert.equal(resolveSceneAffordancesRecord({
    scene: {
      location: { entityId: currentScene.locationId },
      conversation: { entityId: currentScene.conversationId },
    },
    items: [{ ...item, summary: "   " }],
  }, currentScene, "player"), null);
});

test("known ways onward require admitted exact route and destination subjects", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const requestedKinds = [];
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/dnd2024.game.core.world.route`)) {
        requestedKinds.push("dnd2024.game.core.world.route");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "dnd2024.game.core.world.route",
          valueJson: JSON.stringify({
            status: "active",
            summary: "CANARY GM ROUTE SUMMARY",
            visibility: "gm",
            mode: "on-foot",
            durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/dnd2024.game.core.world.route.availability`)) {
        requestedKinds.push("dnd2024.game.core.world.route.availability");
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "dnd2024.game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith(`/${destinationId}/components/dnd2024.game.core.world.location`)) {
        return response(200, {
          entityId: destinationId,
          qualifiedTypeId: "dnd2024.game.core.world.location",
          valueJson: JSON.stringify({
            kind: "settlement", status: "active", summary: "A known port.", visibility: "public",
          }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        requestedKinds.push(kind);
        const targets = {
          "dnd2024.game.core.world.route.in-world": "world.thalorien",
          "dnd2024.game.core.world.route.from": originId,
          "dnd2024.game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [
        { text: "The Crownmere road is open.", stance: "known", presentationKind: "statement",
          subject: { id: routeId, name: "Crownmere road" } },
        { text: "Crownmere is a known port.", stance: "known", presentationKind: "statement",
          subject: { id: destinationId, name: "Crownmere" } },
      ],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });

  assert.deepEqual(routes, [{
    id: routeId,
    originId,
    destinationId,
    destinationName: "Crownmere",
    detail: "The Crownmere road is open.",
    mode: "on-foot",
    durationMinutes: 45,
  }]);
  assert.equal(JSON.stringify(routes).includes("CANARY"), false);
  assert.equal(requestedKinds.every((kind) => kind.startsWith("dnd2024.")), true);
});

test("known ways onward fail closed without destination knowledge", async () => {
  const routeId = "route.thalorien.brackenford-to-crownmere";
  const originId = "location.thalorien.brackenford";
  const destinationId = "location.thalorien.crownmere";
  const routes = await readKnownOpenRoutes({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      if (path.endsWith(`/${routeId}/components/dnd2024.game.core.world.route`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "dnd2024.game.core.world.route",
          valueJson: JSON.stringify({
            status: "active", summary: "A road.", visibility: "public", mode: "on-foot", durationMinutes: 45,
          }),
        });
      }
      if (path.endsWith(`/${routeId}/components/dnd2024.game.core.world.route.availability`)) {
        return response(200, {
          entityId: routeId,
          qualifiedTypeId: "dnd2024.game.core.world.route.availability",
          valueJson: JSON.stringify({ status: "open" }),
        });
      }
      if (path.endsWith("/relationships")) {
        const kind = requested.searchParams.get("qualifiedKind");
        const targets = {
          "dnd2024.game.core.world.route.in-world": "world.thalorien",
          "dnd2024.game.core.world.route.from": originId,
          "dnd2024.game.core.world.route.to": destinationId,
        };
        return response(200, { items: [{ fromEntityId: routeId, toEntityId: targets[kind], qualifiedKind: kind }] });
      }
      return response(404, {});
    },
    origin: "http://localhost:6217",
    entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    worldId: "world.thalorien",
    currentLocationId: originId,
    perspective: "player",
    projectedKnowledge: {
      status: "ready",
      entries: [{
        text: "The road is known.", stance: "known", presentationKind: "statement",
        subject: { id: routeId, name: "Road" },
      }],
      locations: [],
    },
    locationDirectory: [{ id: originId, name: "Brackenford" }, { id: destinationId, name: "Crownmere" }],
  });
  assert.deepEqual(routes, []);
});

test("conversation current scene excludes unapproved participants and summary from Player", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const value = await readConversationCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      if (requested.pathname.endsWith("/interaction.brackenford.parley")) {
        return response(200, { entityId: "interaction.brackenford.parley", name: "Gatehouse parley" });
      }
      if (requested.pathname.endsWith("/components/dnd2024.game.core.world.interaction")) {
        return response(200, {
          entityId: "interaction.brackenford.parley",
          qualifiedTypeId: "dnd2024.game.core.world.interaction",
          valueJson: JSON.stringify({
            kind: "conversation",
            status: "accepted",
            summary: "CANARY DM CONVERSATION SUMMARY",
          }),
        });
      }
      if (requested.pathname.endsWith("/relationships")) {
        return response(200, { items: [
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.hero",
            qualifiedKind: "dnd2024.game.core.world.interaction.participant",
          },
          {
            fromEntityId: "interaction.brackenford.parley",
            toEntityId: "actor.secret-npc",
            qualifiedKind: "dnd2024.game.core.world.interaction.participant",
          },
        ] });
      }
      if (requested.pathname.endsWith("/actor.hero")) {
        return response(200, { entityId: "actor.hero", name: "Hero" });
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    conversationId: "interaction.brackenford.parley",
    perspective: "player",
    authorizedActorIds: new Set(["actor.hero"]),
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "conversation",
    conversation: {
      id: "interaction.brackenford.parley",
      name: "Gatehouse parley",
      participants: [{ id: "actor.hero", name: "Hero" }],
    },
  });
  assert.equal(JSON.stringify(value).includes("CANARY"), false);
  assert.equal(JSON.stringify(value).includes("secret-npc"), false);
});

test("combat current scene reads exact locked Initiative without inventing a turn", async () => {
  const entityRoot = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
  const encounterId = "encounter.brackenford.ambush";
  const participationId = "participation.brackenford.hero";
  const value = await readCombatCurrentScene({
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const kind = requested.searchParams.get("qualifiedKind");
      if (requested.pathname.endsWith(`/entities/${encounterId}`)) {
        return response(200, { entityId: encounterId, name: "Brackenford ambush" });
      }
      if (requested.pathname.endsWith(`/entities/${encounterId}/components/dnd2024.encounter.definition`)) {
        return response(200, {
          entityId: encounterId,
          qualifiedTypeId: "dnd2024.encounter.definition",
          valueJson: JSON.stringify({ environment: { entityId: "location.thalorien.brackenford" } }),
        });
      }
      if (requested.pathname.endsWith("/relationships") && kind === "dnd2024.encounter.has-participation") {
        return response(200, { items: [{
          fromEntityId: encounterId,
          toEntityId: participationId,
          qualifiedKind: kind,
        }] });
      }
      if (requested.pathname.endsWith("/relationships") &&
          ["dnd2024.encounter.active-round", "dnd2024.encounter.active-turn"].includes(kind)) {
        return response(200, { items: [] });
      }
      if (requested.pathname.endsWith(`/entities/${participationId}/components/dnd2024.encounter.participation`)) {
        return response(200, {
          entityId: participationId,
          qualifiedTypeId: "dnd2024.encounter.participation",
          valueJson: JSON.stringify({
            membershipRelationship: {
              stateSpaceId: "dnd2024-main",
              fromEntityId: encounterId,
              toEntityId: participationId,
              qualifiedKind: "dnd2024.encounter.has-participation",
            },
            status: "active",
          }),
        });
      }
      if (requested.pathname.endsWith(`/entities/${participationId}/components/dnd2024.combat.initiative`)) {
        return response(200, {
          entityId: participationId,
          qualifiedTypeId: "dnd2024.combat.initiative",
          valueJson: JSON.stringify({
            encounter: { entityId: encounterId }, status: "locked", result: 17, tieBreakOrder: 0,
          }),
        });
      }
      if (requested.pathname.endsWith("/relationships") &&
          kind === "dnd2024.encounter.participation.for-actor") {
        return response(200, { items: [{
          fromEntityId: participationId,
          toEntityId: "actor.hero",
          qualifiedKind: kind,
        }] });
      }
      if (requested.pathname.endsWith("/entities/actor.hero")) {
        return response(200, { entityId: "actor.hero", name: "Hero" });
      }
      throw new Error(`Unexpected request ${requested}`);
    },
    origin: "http://localhost:6217",
    entityRoot,
    encounterId,
    stateSpaceId: "dnd2024-main",
    perspective: "player",
    authorizedActorIds: new Set(["actor.hero"]),
  });
  assert.deepEqual(value, {
    status: "ready",
    kind: "combat",
    combat: {
      id: encounterId,
      name: "Brackenford ambush",
      participants: [{ id: "actor.hero", name: "Hero", initiative: 17, active: false }],
    },
  });
});

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
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/chronology") {
        return response(200, {
          status: "ready",
          perspective: "player",
          entries: [{
            id: "chronology-1",
            occurredAtMinute: 42,
            dateLabel: "Year 412",
            precision: "exact",
            title: "The Gate Dedication",
            summary: "The rebuilt northern gate was dedicated.",
          }],
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
            mediaOwnerId: "clue.thalorien.brackenford.waystone",
          }],
          locations: [{
            name: "Brackenford",
            entries: [{
              text: "Brackenford\nA frontier village beside the old forest.",
              stance: "known",
              presentationKind: "statement",
              mediaOwnerId: "clue.thalorien.brackenford.waystone",
            }],
          }],
        });
      }
      if (path.endsWith("/clue.thalorien.brackenford.waystone/components/dnd2024.game.core.world.media.visual")) {
        return response(200, {
          entityId: "clue.thalorien.brackenford.waystone",
          qualifiedTypeId: "dnd2024.game.core.world.media.visual",
          valueJson: JSON.stringify({
            status: "active",
            slots: {
              handout: {
                variants: { player: mediaVariant("The admitted waystone rubbing") },
                provenance: {
                  kind: "generated",
                  credit: "Reviewed original artwork",
                  source: "reviewed/waystone.png",
                  reviewedOn: "2026-08-30",
                  version: 1,
                },
              },
            },
          }),
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
        visits: [],
    },
    actor: {
      id: "actor.thalorien.brackenford.orban",
      name: "Orban",
      state: "active",
      entries: [{ kind: "class", key: "bard", label: "Provisional Bard direction" }],
    },
    currentSituation: {
      status: "unavailable",
      message: "No authoritative current scene has been recorded for this campaign.",
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
        media: {
          handout: {
            imageUrl: `/components/media/sha256.${MEDIA_HASH}.png`,
            alt: "The admitted waystone rubbing",
            width: 1024,
            height: 1536,
          },
        },
      }],
      locations: [{
        name: "Brackenford",
        entries: [{
          text: "Brackenford\nA frontier village beside the old forest.",
          stance: "known",
          presentationKind: "statement",
          media: {
            handout: {
              imageUrl: `/components/media/sha256.${MEDIA_HASH}.png`,
              alt: "The admitted waystone rubbing",
              width: 1024,
              height: 1536,
            },
          },
        }],
      }],
    },
    chronology: {
      status: "ready",
      perspective: "player",
      entries: [{
        id: "chronology-1",
        occurredAtMinute: 42,
        dateLabel: "Year 412",
        precision: "exact",
        title: "The Gate Dedication",
        summary: "The rebuilt northern gate was dedicated.",
      }],
    },
  });
  assert.deepEqual(calls, [
    "/api/audience-context",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/campaign.thalorien.brackenford",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.root",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/campaign.thalorien.brackenford/components/dnd2024.game.core.campaign.current-scene",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban/components/dnd2024.playtest-character-record",
    "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge",
    "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/chronology",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/clue.thalorien.brackenford.waystone/components/dnd2024.game.core.world.media.visual",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
    "/api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.thalorien.brackenford.orban/components",
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
  assert.equal(calls.some((path) => path.endsWith("/campaign.thalorien.brackenford/chronology")), true);
  assert.equal(calls.some((path) => path.includes("has-character-participation")), false);
  assert.equal(calls.some((path) => path.includes("clue.") && path.includes("media.visual")), false);
  assert.equal(calls.length, 18);
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
      return response(200, {
        status: "ready",
        entries: [{
          text: "The waystone shard is warm.",
          stance: "known",
          presentationKind: "evidence",
          subject: { id: "location.thalorien.brackenford", name: "Brackenford" },
        }],
        locations: [],
      });
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
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/relationships" &&
        requested.searchParams.get("fromEntityId") === "session.thalorien.brackenford.1" &&
        requested.searchParams.get("qualifiedKind") ===
          "dnd2024.game.core.campaign.record.references-world-entity") {
      return response(200, {
        items: [{
          fromEntityId: "session.thalorien.brackenford.1",
          toEntityId: "location.thalorien.brackenford",
          qualifiedKind: "dnd2024.game.core.campaign.record.references-world-entity",
        }],
      });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/relationships" &&
        requested.searchParams.get("fromEntityId") === "campaign.thalorien.brackenford" &&
        requested.searchParams.get("qualifiedKind") ===
          "dnd2024.game.core.campaign.has-location-visit") {
      return response(200, {
        items: [{
          fromEntityId: "campaign.thalorien.brackenford",
          toEntityId: "campaign-visit.thalorien.brackenford.village",
          qualifiedKind: "dnd2024.game.core.campaign.has-location-visit",
        }],
      });
    }
    if (path === "/api/applications/dnd2024/state-spaces/dnd2024-main/relationships" &&
        requested.searchParams.get("fromEntityId") === "campaign-visit.thalorien.brackenford.village" &&
        requested.searchParams.get("qualifiedKind") ===
          "dnd2024.game.core.campaign.location-visit.at-location") {
      return response(200, {
        items: [{
          fromEntityId: "campaign-visit.thalorien.brackenford.village",
          toEntityId: "location.thalorien.brackenford",
          qualifiedKind: "dnd2024.game.core.campaign.location-visit.at-location",
        }],
      });
    }
    if (path.endsWith("/campaign-visit.thalorien.brackenford.village/components/dnd2024.game.core.campaign.location-visit")) {
      return response(200, {
        entityId: "campaign-visit.thalorien.brackenford.village",
        qualifiedTypeId: "dnd2024.game.core.campaign.location-visit",
        valueJson: JSON.stringify({
          firstVisitedMinute: 120,
          lastVisitedMinute: 360,
          visitCount: 2,
          status: "departed",
          summary: "The frontier village beside the old road.",
          memory: "The party earned the village's trust.",
          gmContext: "The waystone is waking.",
        }),
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
  assert.deepEqual(dm.campaign.sessions[0].worldEntityIds, ["location.thalorien.brackenford"]);
  assert.deepEqual(dm.campaign.visits, [{
    id: "campaign-visit.thalorien.brackenford.village",
    locationId: "location.thalorien.brackenford",
    firstVisitedMinute: 120,
    lastVisitedMinute: 360,
    visitCount: 2,
    status: "departed",
    summary: "The frontier village beside the old road.",
    memory: "The party earned the village's trust.",
    gmContext: "The waystone is waking.",
  }]);
  assert.deepEqual(dm.knowledge.entries[0].subject, {
    id: "location.thalorien.brackenford",
    name: "Brackenford",
  });
  assert.equal("gmContext" in playerPreview.campaign.chapters[0], false);
  assert.equal("gmContext" in playerPreview.campaign.arcs[0], false);
  assert.deepEqual(playerPreview.campaign.sessions, []);
  assert.deepEqual(playerPreview.campaign.visits, []);
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

test("reads canonical character components, class membership, and bounded direct inventory", async () => {
  const campaignId = "campaign.thalorien.brackenford";
  const actorId = "actor.thalorien.brackenford.orban";
  const membershipId = `${actorId}.class-membership.bard`;
  const itemId = `${actorId}.item.gold`;
  const component = (entityId, qualifiedTypeId, value) => response(200, {
    entityId,
    qualifiedTypeId,
    valueJson: JSON.stringify(value),
  });
  const canonicalComponents = {
    "dnd2024.character.identity": { biography: "Raised by performers." },
    "dnd2024.character.origin-selections": {
      speciesRef: { entityId: "dnd2024.content.species.human" },
      backgroundRef: { entityId: "dnd2024.content.background.criminal" },
    },
    "dnd2024.creature.ability-scores": {
      scores: { "dnd2024.vocabulary.ability.charisma": 17 },
    },
    "dnd2024.creature.hit-points": { current: 9, maximum: 9 },
  };

  const value = await readGameServerContext({
    serverOrigin: "http://localhost:6217",
    requestedPerspective: "player",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const path = requested.pathname;
      const fromEntityId = requested.searchParams.get("fromEntityId");
      const qualifiedKind = requested.searchParams.get("qualifiedKind");
      if (path === "/api/audience-context") return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId,
        actorId,
        role: "actor",
      });
      if (path.endsWith(`/${campaignId}/components/dnd2024.game.core.campaign.root`)) {
        return component(campaignId, "dnd2024.game.core.campaign.root", {
          status: "active",
          premise: "A live campaign.",
          partyGoals: [],
          toneAndBoundaries: [],
        });
      }
      if (path.endsWith(`/${campaignId}`)) return response(200, { entityId: campaignId, name: "Brackenford" });
      if (path.endsWith(`/${actorId}/components/dnd2024.playtest-character-record`)) return response(404, {});
      if (path.endsWith(`/${actorId}/components`)) {
        return response(200, { items: Object.keys(canonicalComponents).map((qualifiedTypeId) => ({ qualifiedTypeId })) });
      }
      for (const [qualifiedTypeId, state] of Object.entries(canonicalComponents)) {
        if (path.endsWith(`/${actorId}/components/${qualifiedTypeId}`)) {
          return component(actorId, qualifiedTypeId, state);
        }
      }
      if (path.endsWith(`/${actorId}`)) return response(200, { entityId: actorId, name: "Orban" });
      if (path.endsWith("/relationships") && fromEntityId === actorId &&
          qualifiedKind === "dnd2024.character.has-class-membership") {
        return response(200, { items: [{ fromEntityId: actorId, toEntityId: membershipId, qualifiedKind }] });
      }
      if (path.endsWith(`/${membershipId}/components/dnd2024.character.class-membership`)) {
        return component(membershipId, "dnd2024.character.class-membership", {
          classRef: { entityId: "dnd2024.content.class.bard" },
          level: 1,
        });
      }
      if (path.endsWith(`/${membershipId}`)) return response(200, { entityId: membershipId, name: "Bard membership" });
      if (path.endsWith("/containments") && requested.searchParams.get("containerEntityId") === actorId) {
        return response(200, { items: [{ containedEntityId: itemId, containerEntityId: actorId, slot: "inventory.currency" }] });
      }
      if (path.endsWith(`/${itemId}/components/dnd2024.core.definition-link`)) {
        return component(itemId, "dnd2024.core.definition-link", {
          definition: { entityId: "dnd2024.content.item.currency.gold-piece" },
        });
      }
      if (path.endsWith(`/${itemId}/components/dnd2024.item.quantity`)) {
        return component(itemId, "dnd2024.item.quantity", { current: 45 });
      }
      if (path.endsWith(`/${itemId}/components/dnd2024.item.equipment`)) return response(404, {});
      if (path.endsWith(`/${itemId}`)) return response(200, { entityId: itemId, name: "Starting Gold" });
      if (path === "/api/applications/dnd2024/campaigns/campaign.thalorien.brackenford/knowledge") {
        return response(200, { status: "empty", entries: [], locations: [] });
      }
      if (path.endsWith("/entities")) {
        return response(200, { items: [
          { entityId: campaignId, name: "Brackenford" },
          { entityId: actorId, name: "Orban" },
        ], nextCursor: null });
      }
      if (path.endsWith("/relationships")) return response(200, { items: [] });
      return response(404, {});
    },
  });

  assert.equal(value.party.length, 1);
  assert.equal(value.party[0].canonical.classes[0].classId, "dnd2024.content.class.bard");
  assert.equal(value.party[0].canonical.abilities[0].score, 17);
  assert.deepEqual(value.party[0].canonical.inventory, [{
    id: itemId,
    name: "Starting Gold",
    definitionId: "dnd2024.content.item.currency.gold-piece",
    quantity: 45,
    slot: "inventory.currency",
    equipmentSlots: [],
  }]);
  assert.equal(value.party[0].entries.length, 0);
});
