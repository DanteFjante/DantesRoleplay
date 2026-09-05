import { run } from './campaign-simulation-preflight.mjs';
import { randomUUID, createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { readFile, readdir, cp, mkdir, writeFile } from 'node:fs/promises';
import { join, dirname, basename, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fixture, ids, definitionFingerprint, privateMarkers } from './campaign-simulation-fixture.mjs';
import { scriptedModel, playerText, guideText } from './campaign-simulation-model.mjs';

export async function scenario(api, channel = 'mcp') {
  const { tool, evidence } = api;
  const applicationId = 'dnd2024';
  const stateSpaceId = 'dnd2024-slice19';
  async function commit(kind, payload) {
    const args = { kind, intent: 'Run reviewed disposable Slice 19 fixture',
      payload: JSON.stringify({ requestToken: randomUUID().replaceAll('-', ''), ...payload }) };
    await tool('commit', { ...args, dryRun: true });
    return tool('commit', { ...args, dryRun: false });
  }
  for (const [id, baseApplications] of [['game', []], [applicationId, ['game']]]) {
    await commit('system.application.register', { applicationId: id, displayName: id,
      description: 'Disposable Slice 19 campaign', baseApplications, expectedFingerprint: null });
  }
  await commit('system.source.register', { applicationId, sourceId: 'dnd2024-core', allowedRootId: 'repository',
    relativePathOrGlob: 'catalog/applications/dnd2024/**/*', trust: 'trusted', precedence: 0,
    logicalIdentity: 'dnd2024-core-catalog', expectedFingerprint: null });
  const { schemaVersion, ...extension } = JSON.parse(await readFile(join(api.repo, 'catalog/extensions/dnd2024/legacy-equipment/extension-package.json'), 'utf8'));
  await commit('system.source.register', { applicationId, sourceId: extension.sourceIds[0], allowedRootId: 'repository',
    relativePathOrGlob: 'catalog/extensions/dnd2024/legacy-equipment/content/**/*', trust: 'trusted', precedence: 10,
    logicalIdentity: 'slice19-reviewed-legacy-equipment', expectedFingerprint: null });
  await commit('system.extension.register', { ...extension, expectedFingerprint: null });
  const initial = await fixture(api.repo);
  const componentFiles = await readdir(join(api.repo, 'catalog/applications/dnd2024/components'));
  for (const file of componentFiles.filter(name => name.endsWith('.schema.json'))) {
    const qualifiedTypeId = file.slice(0, -'.schema.json'.length);
    await commit('system.component-type.register', { applicationId, qualifiedTypeId,
      schemaJson: await readFile(join(api.repo, 'catalog/applications/dnd2024/components', file), 'utf8'), expectedSchemaHash: null });
  }
  const baseTypes = new Set(initial.entities.flatMap(entity => entity.components.map(value => value.qualifiedTypeId)).filter(id => id.startsWith('game.')));
  baseTypes.add('game.core.world.fact');
  for (const name of await readdir(join(api.repo, 'catalog/components/game/core/campaign'), { recursive: true }))
    if (name.endsWith('.schema.json')) baseTypes.add('game.core.campaign.' + name.slice(0, -'.schema.json'.length).replaceAll(/[\\/]/g, '.'));
  for (const qualifiedTypeId of baseTypes) await commit('system.component-type.register', { applicationId: 'game', qualifiedTypeId,
    schemaJson: await readFile(join(api.repo, 'catalog/components', qualifiedTypeId.replaceAll('.', '/') + '.schema.json'), 'utf8'), expectedSchemaHash: null });
  const preview = await tool('query', { kind: 'system.application-preview', applicationId });
  await evidence('activation-preview', preview);
  assert.ok(preview.isValid, 'Application preview must be valid');
  assert.ok(preview.extensionIds.includes('legacy-equipment'), 'Registered extension must participate automatically');
  const activation = await commit('system.application.activate', { applicationId,
    previewFingerprint: preview.previewFingerprint, expectedActiveFingerprint: null });
  await commit('system.state-space.adopt-legacy', { applicationId, stateSpaceId,
    activeFingerprint: activation.activation.activationFingerprint,
    entityIds: ['slice19-world'], componentMappings: [], relationshipMappings: [] });
  await commit('system.world-state.sync', { applicationId, stateSpaceId, rootEntityId: ids.world,
    entities: initial.entities, relationships: initial.relationships });
  const extensionWinner = await tool('query', { kind: 'system.catalog.record', applicationId, collection: applicationId,
    id: 'dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1' });
  assert.equal(extensionWinner.record.summary.sourceId, extension.sourceIds[0]);
  async function read(entityId) {
    const data = await tool('query', { kind: 'entities', applicationId, stateSpaceId, id: entityId });
    assert.equal(data.entities.length, 1);
    return data.entities[0];
  }
  async function component(entityId, type) {
    const entity = await read(entityId);
    const value = entity.components.find(component => component.qualifiedTypeId === type);
    assert.ok(value, `${entityId} is missing ${type}`);
    return value.value;
  }
  const readModelPath = (id, query) => `/api/applications/${applicationId}/state-spaces/${stateSpaceId}/entities/${id}/read-models/${query}`;
  let queryStep = 0;
  async function queryPlan(qualifiedId, roleBindings, learn = false) {
    const record = await tool('query', { kind: 'system.catalog.record', applicationId, collection: applicationId, id: qualifiedId });
    const key = `slice19-query-${++queryStep}`;
    const intent = { idempotencyKey: key, intentText: `Inspect current ${qualifiedId}`, roleHints: roleBindings,
      conversationFactReferences: [], maximumPlanSteps: 1, plannerPreference: 'automatic' };
    const proposal = { command: 'propose', steps: [{ stepId: 'inspect', kind: 'query', qualifiedId,
      version: record.record.summary.version, fingerprint: record.record.summary.contentFingerprint, dependsOn: [], roleBindings, input: {} }] };
    const plan = await tool('query', { kind: 'system.interaction-plan', applicationId,
      request: JSON.stringify({ operation: 'submit', stateSpaceId, sessionContextId: 'slice19-session', intent, proposal }) });
    assert.ok(plan.proposal && plan.proposalFingerprint, JSON.stringify(plan));
    const execution = await tool('commit', { kind: 'system.interaction-execute', intent: intent.intentText,
      payload: JSON.stringify({ applicationId, stateSpaceId, resolutionReceiptId: plan.receipt.receipt.id,
        proposalFingerprint: plan.proposalFingerprint, idempotencyKey: key + '-execute', proposal: plan.proposal,
        stopOnFailure: true, learn, learningIntent: learn ? intent : null }) });
    assert.ok(execution.successful, JSON.stringify(execution));
    assert.equal(execution.queryResults.length, 1);
    await evidence('verified-query-context-and-receipts', { plan, execution });
    return execution.queryResults[0];
  }
  let inventoryRecipe;
  let recipeRun = 0;
  async function plannedInventory() {
    const started = performance.now();
    const idempotencyKey = `slice19-inventory-plan-${++recipeRun}`;
    const intent = { idempotencyKey, intentText: 'Inspect my carried supplies',
      roleHints: { root: ids.actor, actor: ids.actor, campaign: ids.campaign },
      maximumPlanSteps: 1, plannerPreference: 'automatic', conversationFactReferences: [] };
    let proposal = null;
    if (!inventoryRecipe) {
      const record = await tool('query', { kind: 'system.catalog.record', applicationId, collection: applicationId, id: 'dnd2024.mechanic.inventory.read' });
      proposal = { command: 'propose', steps: [{ stepId: 'inventory', kind: 'action', qualifiedId: record.record.summary.qualifiedId,
        version: record.record.summary.version, fingerprint: record.record.summary.contentFingerprint,
        dependsOn: [], roleBindings: { root: ids.actor }, input: {} }] };
    }
    const plan = await tool('query', { kind: 'system.interaction-plan', applicationId, request: JSON.stringify({
      operation: proposal ? 'submit' : 'resolve', stateSpaceId, sessionContextId: 'slice19-session', intent, proposal }) });
    assert.ok(plan.proposal && plan.proposalFingerprint, JSON.stringify(plan));
    if (inventoryRecipe) {
      assert.equal(plan.recipeReference.id, inventoryRecipe);
      assert.ok(plan.evidence.includes('verified-recipe'));
      assert.ok(plan.evidence.some(value => /^context:[A-F0-9]{64}$/.test(value)), 'Resolved planning must record a real context-pack fingerprint');
      assert.ok(plan.evidence.some(value => value.startsWith('usage:rounds=0,')), 'Verified recipe reuse must avoid model rounds');
    }
    const before = await read(ids.actor);
    const execution = await tool('commit', { kind: 'system.interaction-execute', intent: intent.intentText, payload: JSON.stringify({
      applicationId, stateSpaceId, resolutionReceiptId: plan.receipt.receipt.id,
      proposalFingerprint: plan.proposalFingerprint, idempotencyKey: idempotencyKey + '-execute', proposal: plan.proposal,
      stopOnFailure: true, learn: !inventoryRecipe, learningIntent: inventoryRecipe ? null : intent }) });
    assert.ok(execution.successful, JSON.stringify(execution));
    assert.deepEqual(await read(ids.actor), before, 'Inventory inspection must not mutate the character');
    if (!inventoryRecipe) {
      const candidate = execution.learning?.recipe;
      assert.ok(candidate, JSON.stringify(execution.learning));
      const reviewed = await tool('commit', { kind: 'system.interaction-recipe-review', intent: 'Verify the demonstrated read-only fixture inventory route',
        payload: JSON.stringify({ requestToken: randomUUID().replaceAll('-', ''), applicationId, recipeId: candidate.id,
          expectedVersion: candidate.version, decision: 'verify', reason: 'Disposable fixture only: exact inspected inventory contract executed successfully without state changes.' }) });
      assert.equal(reviewed.disposition, 'Created');
      inventoryRecipe = candidate.id;
    }
    const recipe = await tool('query', { kind: 'system.interaction-recipes', applicationId, id: inventoryRecipe });
    assert.equal(recipe.items[0].status, 'Verified');
    await evidence('planned-inventory-recipe', { plan, execution, recipe, elapsedMilliseconds: performance.now() - started });
  }
  let step = 0;
  async function action(qualifiedMechanicId, roleEntityIds, input) {
    const path = `/api/applications/${applicationId}/state-spaces/${stateSpaceId}/mechanics/${qualifiedMechanicId}`;
    const descriptor = await api.http(path);
    const idempotencyKey = `1900000000000000${String(++step).padStart(16, '0')}`;
    const payload = JSON.stringify({ idempotencyKey, applicationId, stateSpaceId, qualifiedMechanicId,
      mechanicVersion: descriptor.version, contentFingerprint: descriptor.contentFingerprint, roleEntityIds, input });
    let prepared;
    let webRequest;
    if (channel === 'web') {
      prepared = await api.http(path + '/prepare', { idempotencyKey: idempotencyKey + '-prepare', roleEntityIds, input });
      assert.ok(prepared.ready, JSON.stringify(prepared));
      webRequest = { resolutionReceiptId: prepared.receipt.id, proposalFingerprint: prepared.proposalFingerprint,
        idempotencyKey, proposal: prepared.proposal };
    }
    const before = await component(ids.world, 'game.core.world.clock');
    const raw = channel === 'web' ? await api.http(path + '/execute', webRequest)
      : await tool('commit', { kind: 'application.action.execute', intent: `Slice 19 step ${step}`, payload });
    if (channel === 'web') { assert.ok(raw.successful, JSON.stringify(raw)); assert.equal(raw.actionResults.length, 1); assert.equal(raw.actionResults[0].disposition, 0); }
    const result = channel === 'web' ? { affectedEntityIds: raw.actionResults[0].affectedEntityIds,
      receipt: { ...raw.actionResults[0], disposition: 'succeeded' } } : raw;
    const after = await component(ids.world, 'game.core.world.clock');
    const affected = [];
    for (const id of result.affectedEntityIds) affected.push(await read(id));
    const events = await tool('query', { kind: 'events', rootOperationId: result.receipt.operationId, limit: 100 });
    const expectedEvent = {
      'dnd2024.mechanic.travel.execute': 'dnd2024.travel.arrived',
      'dnd2024.mechanic.trap.trigger': 'dnd2024.hazard.trap-triggered',
      'dnd2024.mechanic.social.attitude.transition': 'dnd2024.social.attitude-changed',
      'dnd2024.mechanic.rest.complete': 'dnd2024.rest.completed',
      'dnd2024.mechanic.downtime.complete': 'dnd2024.downtime.completed',
    }[qualifiedMechanicId];
    if (expectedEvent) assert.equal(events.events.filter(event => event.typeId === expectedEvent).length, 1);
    assert.ok(events.events.length < 100, 'Evidence must not be silently truncated');
    assert.ok(!JSON.stringify(events).includes(guideText), 'Verbatim speech is not a mechanical event');
    const replay = channel === 'web' ? await api.http(path + '/execute', webRequest)
      : await tool('commit', { kind: 'application.action.execute', intent: `Replay Slice 19 step ${step}`, payload });
    assert.equal(result.receipt.disposition, 'succeeded');
    if (channel === 'web') {
      assert.equal(replay.receipt.code, 'INTERACTION_RECEIPT_REPLAY');
      assert.equal(replay.receipt.receipt.id, raw.receipt.receipt.id);
      assert.equal(replay.actionResults.length, 0, 'An execution receipt replay must not execute actions');
    } else {
      assert.equal(replay.receipt.disposition, 'replayed');
      assert.equal(replay.receipt.operationId, result.receipt.operationId);
    }
    assert.deepEqual(await tool('query', { kind: 'events', rootOperationId: result.receipt.operationId, limit: 100 }), events, 'Replay must not duplicate events');
    const replayedEntities = [];
    for (const id of result.affectedEntityIds) replayedEntities.push(await read(id));
    assert.deepEqual(replayedEntities, affected, 'Replay must not mutate affected entities');
    assert.deepEqual(await component(ids.world, 'game.core.world.clock'), after, 'Replay must not advance time');
    assert.ok(after.currentMinute >= before.currentMinute, 'Clock must be monotonic');
    await evidence(`action-${step}-receipt`, { channel, prepared, result: raw, replay, before, after, events });
    return result;
  }
  api.begin('exploration');
  assert.equal((await component(ids.origin, 'game.core.world.location')).status, 'active');
  await queryPlan('dnd2024.query.campaign-resume', { campaign: ids.campaign });
  api.pass('exploration');
  api.begin('travel-hazard');
  await action('dnd2024.mechanic.travel.execute', { traveller: ids.actor, origin: ids.origin, destination: ids.destination, route: ids.route, world: ids.world }, {
    travel: { journeyId: 'slice19-journey', exposureScheduleId: 'slice19-exposures', mode: 'walk', pace: 'normal',
      expectedRouteRevision: 1, expectedRouteFingerprint: definitionFingerprint, expectedClockRevision: 1 },
  });
  assert.equal((await component(ids.world, 'game.core.world.clock')).currentMinute, 220);
  await action('dnd2024.mechanic.campaign.current-scene.set', { campaign: ids.campaign, world: ids.world, location: ids.destination }, { mode: 'move' });
  await action('dnd2024.mechanic.trap.trigger', { trap: ids.hazard, definition: ids['hazard-definition'], target: ids.actor }, {
    expectedDefinitionRevision: 1,
    damageEffects: [{ amount: 3, damageType: 'bludgeoning', saveSucceeded: false, successfulSaveBehavior: 'full' }],
    conditionEffects: [{ mode: 'apply', conditions: ['prone'] }],
  });
  assert.equal((await component(ids.actor, 'dnd2024.creature.hit-points')).current, 17);
  assert.equal((await component(ids.hazard, 'dnd2024.hazard.trap-state')).activationCount, 1);
  assert.ok((await component(ids.actor, 'dnd2024.conditions')).entries.some(value => value.condition === 'prone'));
  api.pass('travel-hazard');
  api.begin('conversation-social');
  const conversation = await api.http(`/api/applications/${applicationId}/conversations`, { stateSpaceId, sessionContextId: 'slice19-session' });
  const spoken = await api.http(`/api/applications/${applicationId}/conversations/${conversation.id}/turns`, { text: playerText, replaceActiveAgenda: false });
  assert.ok(JSON.stringify(spoken).includes(guideText), 'The actual conversation service must persist the scripted reply');
  const historyPath = `/api/applications/${applicationId}/conversations/${conversation.id}/history`;
  const history = await api.http(historyPath);
  const playPath = `/api/applications/${applicationId}/state-spaces/${stateSpaceId}/play/sessions/slice19-session`;
  const play = await api.http(playPath);
  assert.deepEqual(history.messages.map(value => [value.role, value.text]), [['player', playerText], ['assistant', guideText]]);
  assert.equal(play.knownTruths.length, 1);
  assert.equal(play.knownTruths[0].sourceMessageId, history.messages[1].id);
  assert.equal(play.knownTruths[0].statement, 'The guide welcomed Mira.');
  await evidence('conversation-and-truths', { history, play });
  await action('dnd2024.mechanic.social.attitude.transition', { source: ids.guide, target: ids.actor, campaign: ids.campaign }, {
    relationshipId: 'slice19-social', previousAttitudeId: null, nextAttitudeId: 'dnd2024.vocabulary.attitude.friendly',
    visibility: 'party', expectedRevision: 0, evidenceReceiptIds: [],
    reasonFacts: [{ factId: 'slice19-social-reason', kind: 'reason', summary: 'Mira offered to help the guide.', provenance: 'Reviewed synthetic conversation choice; not a persuasion roll.', visibility: 'party' }],
    consequence: { factId: 'slice19-social-consequence', kind: 'consequence', summary: 'The guide welcomes Mira.', provenance: 'Reviewed synthetic guide decision.', visibility: 'party' },
  });
  api.pass('conversation-social');
  await action('dnd2024.mechanic.social.attitude.transition', { source: ids.actor, target: ids.guide, campaign: ids.campaign }, {
    relationshipId: 'slice19-private-social', previousAttitudeId: null, nextAttitudeId: 'dnd2024.vocabulary.attitude.indifferent',
    visibility: 'gm', expectedRevision: 0, evidenceReceiptIds: [],
    reasonFacts: [{ factId: 'slice19-private-reason', kind: 'reason', summary: 'GM_PRIVATE_REASON', provenance: 'Synthetic GM-only fixture.', visibility: 'gm' }],
  });
  // Conversation persistence is checked separately below; an attitude action alone is not dialogue.
  api.begin('combat-movement');
  await action('dnd2024.mechanic.encounter-turn.start', { encounter: ids.encounter }, { roundId: 'slice19-round', turnId: ids.turn });
  await action('dnd2024.mechanic.encounter.board.place', { encounter: ids.encounter, participation: ids.actor + '-participation' }, {
    expectedBoardRevision: 1, expectedPositionRevision: null,
    position: { anchor: { x: 1, y: 1 }, footprint: { width: 1, height: 1 }, elevationFeet: 0, visibility: 'public' },
  });
  await action('dnd2024.mechanic.encounter.board.place', { encounter: ids.encounter, participation: ids.guide + '-participation' }, {
    expectedBoardRevision: 1, expectedPositionRevision: null,
    position: { anchor: { x: 4, y: 1 }, footprint: { width: 1, height: 1 }, elevationFeet: 0, visibility: 'public' },
  });
  // Explicitly clear the recorded fall before the separate defensive exercise. This is a selected
  // condition-state action, not a claim that tactical movement implements standing automatically.
  await action('dnd2024.mechanic.conditions.write', { subject: ids.actor }, { mode: 'clear', conditions: ['prone'] });
  await action('dnd2024.mechanic.encounter.board.move', { subject: ids.actor, encounter: ids.encounter, participation: ids.actor + '-participation' }, {
    expectedBoardRevision: 1, expectedPositionRevision: 1, expectedTurnId: ids.turn, path: [{ x: 2, y: 1 }],
    spend: { resource: 'movement', distance: { dimension: 'distance', value: { numerator: 381, denominator: 125 }, unit: { entityId: 'dnd2024.vocabulary.distance-unit.meter' } } },
  });
  assert.deepEqual((await component(ids.actor + '-participation', 'dnd2024.combat.position')).anchor, { x: 2, y: 1 });
  await action('dnd2024.mechanic.encounter-turn.end', { encounter: ids.encounter }, {});
  api.pass('combat-movement');
  api.begin('loot-inventory');
  await action('dnd2024.mechanic.item.transfer', { item: ids.loot, source: ids.destination, destination: ids.backpack }, { slot: 'inventory.supplies' });
  const inventory = await api.http(readModelPath(ids.actor, 'dnd2024.query.character-sheet'));
  assert.ok(JSON.stringify(inventory.data.inventory).includes(ids.loot), 'Transferred loot must appear in canonical nested inventory');
  await plannedInventory();
  await plannedInventory();
  api.pass('loot-inventory');
  api.begin('rest');
  const restRoles = { creature: ids.actor, world: ids.world, policy: initial.restPolicyId };
  await action('dnd2024.mechanic.rest.begin', restRoles, { kind: 'short' });
  await action('dnd2024.mechanic.rest.progress', restRoles, { activity: 'light', minutes: 60 });
  await action('dnd2024.mechanic.rest.complete', restRoles, { hitDice: [] });
  assert.equal((await component(ids.actor, 'dnd2024.rest-completion')).status, 'complete');
  assert.equal((await component(ids.actor, 'dnd2024.creature.hit-points')).current, 17, 'No Hit Dice were selected; do not invent healing');
  assert.equal((await component(ids.world, 'game.core.world.clock')).currentMinute, 280);
  api.pass('rest');
  api.begin('downtime');
  const downtimeRoles = { participant: ids.actor, world: ids.world, definition: ids.downtime };
  const definition = { expectedDefinitionRevision: 1, expectedDefinitionFingerprint: definitionFingerprint };
  await action('dnd2024.mechanic.downtime.begin', downtimeRoles, { ...definition, activityId: 'slice19-service', prerequisiteKeys: [], reservations: [] });
  await action('dnd2024.mechanic.downtime.progress', { participant: ids.actor, world: ids.world }, { minutes: 60, expectedClockRevision: (await component(ids.world, 'game.core.world.clock')).revision });
  await action('dnd2024.mechanic.downtime.complete', downtimeRoles, definition);
  assert.equal((await component(ids.actor, 'dnd2024.downtime.activity')).status, 'completed');
  assert.equal((await component(ids.world, 'game.core.world.clock')).currentMinute, 340);
  api.pass('downtime');
  api.begin('restart-resume');
  const resumeBefore = await api.http(readModelPath(ids.campaign, 'dnd2024.query.campaign-resume'));
  const durableIds = [ids.world, ids.actor, ids.loot, ids.backpack, ids.turn, ids.hazard, 'slice19-social'];
  const beforeRestart = [];
  for (const id of durableIds) beforeRestart.push(await read(id));
  await api.restart();
  const afterRestart = [];
  for (const id of durableIds) afterRestart.push(await read(id));
  assert.deepEqual(afterRestart, beforeRestart, 'Restart must preserve canonical campaign state');
  assert.deepEqual(await api.http(historyPath), history, 'Verbatim conversation must survive restart');
  assert.deepEqual(await api.http(playPath), play, 'Typed play truths must survive restart');
  assert.deepEqual(await api.http(readModelPath(ids.campaign, 'dnd2024.query.campaign-resume')), resumeBefore);
  await plannedInventory();
  await evidence('campaign-resume-state', { beforeRestart, afterRestart });
  api.pass('restart-resume');
  api.begin('web-parity');
  await queryPlan('dnd2024.query.rest-status', restRoles);
  await queryPlan('dnd2024.query.downtime-status', downtimeRoles);
  for (const [query, entity] of [['character-sheet', ids.actor], ['campaign-resume', ids.campaign]]) {
    const qualifiedId = 'dnd2024.query.' + query;
    const contract = JSON.parse((await tool('query', { kind: 'system.catalog.record', applicationId, collection: applicationId, id: qualifiedId })).record.contentJson);
    const actualRole = Object.keys(contract.roles)[0];
    const mcp = await queryPlan(qualifiedId, { [actualRole]: entity });
    const web = await api.http(readModelPath(entity, qualifiedId));
    assert.deepEqual(mcp.output, web.data, `${query} must agree across public contracts`);
    assert.equal(mcp.resultFingerprint, web.resultFingerprint);
    assert.equal(mcp.sourceRevisionFingerprint, web.sourceRevisionFingerprint);
  }
  api.pass('web-parity');
  api.begin('audience-isolation');
  const privateSocial = await api.http(readModelPath(ids.actor, 'dnd2024.query.social-context'));
  assert.ok(JSON.stringify(privateSocial).includes('GM_PRIVATE_REASON'));
  await api.restart({ Knowledge__LocalPlayer__Role: 'Actor' });
  const playerSheet = await api.http(readModelPath(ids.actor, 'dnd2024.query.character-sheet'));
  const playerSocial = await api.http(readModelPath(ids.actor, 'dnd2024.query.social-context'));
  const playerResume = await api.http(readModelPath(ids.campaign, 'dnd2024.query.campaign-resume'), undefined, 403);
  assert.equal(playerResume.code, 'READ_MODEL_AUDIENCE_DENIED', 'The actor seat cannot bypass the campaign-level read gate');
  const playerOutput = JSON.stringify({ playerSheet, playerSocial, playerResume });
  // Prove the private canaries actually exist before asserting their exclusion. These public
  // MCP operator reads are evidence only, never substituted for the actor-bound web responses.
  const privateState = JSON.stringify([await read('slice19-private-fact'), await read('slice19-private-knowledge')]);
  for (const marker of privateMarkers) assert.ok(privateState.includes(marker));
  for (const marker of [...privateMarkers, 'GM_PRIVATE_REASON', 'GM_HIDDEN_WALL', inventoryRecipe,
    'Disposable fixture only: exact inspected inventory contract'])
    assert.ok(!playerOutput.includes(marker), `Player response leaked private or governance material: ${marker}`);
  for (const forbidden of ['draft', 'governance', 'learning', 'recipeReference', 'reviewFingerprint'])
    assert.ok(!new RegExp(`"${forbidden}"\\s*:`).test(playerOutput), `Player response exposed ${forbidden}`);
  for (const entity of [ids.guide, ids.hazard, ids.encounter, 'slice19-private-fact', 'slice19-private-knowledge']) {
    const denied = await api.http(readModelPath(entity, 'dnd2024.query.character-sheet'), undefined, 403);
    assert.equal(denied.code, 'READ_MODEL_AUDIENCE_DENIED');
  }
  api.pass('audience-isolation');
  api.report.channel = channel;
  api.report.outcome = { clock: await component(ids.world, 'game.core.world.clock'),
    hitPoints: await component(ids.actor, 'dnd2024.creature.hit-points'),
    conditions: (await component(ids.actor, 'dnd2024.conditions')).entries,
    position: (await component(ids.actor + '-participation', 'dnd2024.combat.position')).anchor,
    rest: (await component(ids.actor, 'dnd2024.rest-completion')).benefits,
    downtime: (await component(ids.actor, 'dnd2024.downtime.activity')).status,
    inventory: playerSheet.data.inventory,
    dialogue: history.messages.map(value => ({ role: value.role, text: value.text })),
    truths: play.knownTruths.map(value => value.statement) };
}

async function prepareCatalog({ catalog, repo }) {
  await cp(join(repo, 'catalog/event-types/dnd2024'), join(catalog, 'event-types/dnd2024'), { recursive: true });
  // The disposable registry explicitly admits the already-authored application component types.
  // These namespace declarations exist only in this fresh fixture catalog, never the repository.
  const componentFiles = await readdir(join(repo, 'catalog/applications/dnd2024/components'));
  const namespaceIds = new Set(componentFiles.filter(file => file.endsWith('.schema.json'))
    .map(file => file.slice(0, -'.schema.json'.length).split('.').slice(0, -1).join('.')));
  const kindsByNamespace = new Map([...namespaceIds].map(id => [id, new Set(['component-type'])]));
  for (const [directory, extension, kind] of [['mechanics', '.md', 'mechanic'], ['procedures', '.md', 'procedure'], ['queries', '.json', 'document']]) {
    for (const relative of await readdir(join(repo, 'catalog/applications/dnd2024', directory), { recursive: true })) {
      if (!relative.endsWith(extension)) continue;
      const id = basename(relative, extension).split('.').slice(0, -1).join('.');
      if (!id.startsWith('dnd2024.')) continue;
      namespaceIds.add(id);
      if (!kindsByNamespace.has(id)) kindsByNamespace.set(id, new Set());
      kindsByNamespace.get(id).add(kind);
    }
  }
  for (const id of [...namespaceIds]) {
    let parent = id;
    while (parent.includes('.')) {
      parent = parent.split('.').slice(0, -1).join('.');
      namespaceIds.add(parent);
      if (!kindsByNamespace.has(parent)) kindsByNamespace.set(parent, new Set(['document']));
    }
  }
  for (const id of [...namespaceIds].sort()) {
    const file = join(catalog, 'namespaces', id.replaceAll('.', '/'), '_namespace.json');
    let value;
    try { value = JSON.parse(await readFile(file, 'utf8')); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (!value) value = { id, owner: 'dnd2024', description: 'Disposable registry for authored D&D component schemas.',
      allowedKinds: [], aliases: [], enabled: true, reviewStatus: 'reviewed', reviewNote: 'Synthetic Slice 19 registry only; not a production namespace approval.' };
    assert.equal(value.reviewStatus, 'reviewed', `Cannot promote unreviewed namespace ${id}`);
    value.allowedKinds = [...new Set([...value.allowedKinds, ...kindsByNamespace.get(id)])];
    await mkdir(dirname(file), { recursive: true });
    await writeFile(file, JSON.stringify(value));
  }
}

export function compareCampaigns(direct, web) {
  assert.equal(direct?.status, 'passed', 'The MCP campaign must pass');
  assert.equal(web?.status, 'passed', 'The web campaign must pass');
  assert.ok(direct.outcome && web.outcome, 'Both campaigns require measured outcomes');
  assert.deepEqual(web.outcome, direct.outcome, 'Fresh MCP and web campaigns must reach the same authoritative outcome');
  return createHash('sha256').update(JSON.stringify(direct.outcome)).digest('hex').toUpperCase();
}

export async function main() {
  const direct = await run({ scenario: api => scenario(api, 'mcp'), modelDriver: scriptedModel, prepareCatalog });
  const web = direct.status === 'passed'
    ? await run({ scenario: api => scenario(api, 'web'), modelDriver: scriptedModel, prepareCatalog }) : null;
  const acceptance = { scope: 'slice19-campaign-simulation', status: 'blocked',
    modelDriver: 'Scripted conversation response; production planning, actions, persistence and queries.',
    directReport: join(direct.evidenceDirectory, 'report.json'), webReport: web ? join(web.evidenceDirectory, 'report.json') : null,
    exclusions: ['No live data or live activation', 'No model-quality benchmark',
      'Combat scenario covers locked turn lifecycle and tactical movement; broader combat rule regressions remain in the full suite',
      'Short Rest selects no Hit Dice; service downtime grants no invented reward'] };
  try { acceptance.outcomeFingerprint = compareCampaigns(direct, web); acceptance.status = 'passed'; }
  catch (error) { acceptance.failure = error.message; process.exitCode = 1; }
  const path = join(direct.evidenceDirectory, 'acceptance.json');
  const bytes = JSON.stringify(acceptance, null, 2) + '\n';
  await writeFile(path, bytes);
  assert.equal(await readFile(path, 'utf8'), bytes);
  console.log(`Slice 19 acceptance: ${acceptance.status}; ${path}`);
  return acceptance;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
