var subject = ctx.roles.subject;
var input = ctx.input;
var ABILITIES = 'dnd2024.creature.ability-scores';
var ENTITLEMENTS = 'dnd2024.character.feature-entitlements';
var DEX = 'dnd2024.vocabulary.ability.dexterity';
var ALERT = 'dnd2024.feat.alert';
var EPISODE = 'dnd2024.rest-episode';
var POLICY = 'dnd2024.rest-policy';
var WORLD_ROOT = 'game.core.world.root';
var WORLD_CLOCK = 'game.core.world.clock';
var REST_WORLD = 'rest.world';
var STANDARD_POLICY = 'dnd2024.content.rest-policy.standard.v1';
var REST_SOURCE = 'dnd2024.source.srd-5.2.1';
var REST_MAX = 1000000000;

function object(value) {
    return value !== null && !Array.isArray(value) && typeof value === 'object';
}

function closed(value, required, optional) {
    if (!object(value)) return false;
    var allowed = required.concat(optional || []);
    var keys = Object.keys(value);
    for (var i = 0; i < required.length; i++) {
        if (!Object.prototype.hasOwnProperty.call(value, required[i])) return false;
    }
    for (var j = 0; j < keys.length; j++) {
        if (allowed.indexOf(keys[j]) < 0) return false;
    }
    return true;
}

function parse(raw, label) {
    try {
        var value = JSON.parse(raw);
        if (!object(value)) throw 0;
        return value;
    } catch (error) {
        throw new Error(label + ' is corrupt.');
    }
}

function entityRef(value) {
    return closed(value, ['entityId'])
        && typeof value.entityId === 'string'
        && value.entityId.length > 0
        && value.entityId.length <= 200;
}

function sourceRef(value) {
    return closed(value, ['sourceId', 'locator'])
        && typeof value.sourceId === 'string'
        && /^[a-z0-9][a-z0-9.-]+$/.test(value.sourceId)
        && value.sourceId.length <= 200
        && typeof value.locator === 'string'
        && value.locator.trim() === value.locator
        && value.locator.length >= 4
        && value.locator.length <= 200;
}

function integer(value, minimum, maximum) {
    return Number.isSafeInteger(value) && value >= minimum && value <= maximum;
}

function exactSource(value, locator) {
    return closed(value, ['sourceId', 'locator'])
        && value.sourceId === REST_SOURCE
        && value.locator === locator;
}

function validClock(value) {
    return closed(value, ['calendarId', 'currentMinute', 'revision'])
        && typeof value.calendarId === 'string'
        && value.calendarId.length > 0
        && value.calendarId.length <= 100
        && integer(value.currentMinute, 0, REST_MAX)
        && integer(value.revision, 0, 2147483647);
}

function validPolicy(value) {
    return closed(value, ['longRest', 'policyKey', 'policyVersion', 'shortRest', 'sourceRef'])
        && value.policyKey === 'standard'
        && value.policyVersion === 1
        && exactSource(value.sourceRef,
            'Rules Glossary > Long Rest and Short Rest, PDF pages 185 and 187')
        && closed(value.shortRest,
            ['benefits', 'interruptions', 'minimumHitPoints', 'minimumMinutes', 'permittedActivity'])
        && value.shortRest.minimumMinutes === 60
        && value.shortRest.minimumHitPoints === 1
        && value.shortRest.permittedActivity === 'light'
        && JSON.stringify(value.shortRest.interruptions)
            === JSON.stringify(['initiative', 'non-cantrip-spell', 'damage'])
        && closed(value.longRest,
            ['additionalMinutesPerInterruption', 'benefits', 'interruptions',
                'maximumLightActivityMinutes', 'minimumHitPoints', 'minimumMinutes',
                'minimumSleepMinutes', 'partialShortRestMinutes', 'permittedActivity',
                'restartWaitMinutes'])
        && value.longRest.minimumMinutes === 480
        && value.longRest.minimumSleepMinutes === 360
        && value.longRest.maximumLightActivityMinutes === 120
        && value.longRest.minimumHitPoints === 1
        && value.longRest.restartWaitMinutes === 960
        && value.longRest.partialShortRestMinutes === 60
        && value.longRest.additionalMinutesPerInterruption === 60
        && value.longRest.permittedActivity === 'light'
        && JSON.stringify(value.longRest.interruptions)
            === JSON.stringify(['initiative', 'non-cantrip-spell', 'damage',
                'walking-or-physical-exertion']);
}

function validEpisode(value, policy) {
    if (!object(value)
        || value.policyEntityId !== STANDARD_POLICY
        || typeof value.worldId !== 'string'
        || value.worldId.length === 0
        || value.worldId.length > 200
        || !integer(value.startedAtMinute, 0, REST_MAX)
        || !integer(value.observedAtMinute, value.startedAtMinute, REST_MAX)
        || !integer(value.observedClockRevision, 0, 2147483647)
        || (value.status !== 'active' && value.status !== 'ready')) return false;
    var elapsed = value.observedAtMinute - value.startedAtMinute;
    if (value.kind === 'short') {
        return closed(value,
            ['kind', 'lightActivityMinutes', 'observedAtMinute', 'observedClockRevision',
                'policyEntityId', 'requiredMinutes', 'sourceRef', 'startedAtMinute', 'status',
                'worldId'])
            && value.requiredMinutes === policy.shortRest.minimumMinutes
            && integer(value.lightActivityMinutes, 0, REST_MAX)
            && value.lightActivityMinutes === elapsed
            && exactSource(value.sourceRef, 'Rules Glossary > Short Rest, PDF page 187')
            && (value.status === 'ready') === (value.lightActivityMinutes >= value.requiredMinutes);
    }
    if (value.kind !== 'long'
        || !closed(value,
            ['interruptionCount', 'kind', 'lightActivityMinutes', 'observedAtMinute',
                'observedClockRevision', 'policyEntityId', 'requiredMinutes', 'sleepMinutes',
                'sourceRef', 'startedAtMinute', 'status', 'worldId'])
        || !integer(value.sleepMinutes, 0, REST_MAX)
        || !integer(value.lightActivityMinutes, 0, policy.longRest.maximumLightActivityMinutes)
        || !integer(value.interruptionCount, 0, 16666658)
        || value.requiredMinutes !== policy.longRest.minimumMinutes
            + policy.longRest.additionalMinutesPerInterruption * value.interruptionCount
        || value.requiredMinutes > REST_MAX
        || value.sleepMinutes + value.lightActivityMinutes !== elapsed
        || !exactSource(value.sourceRef, 'Rules Glossary > Long Rest, PDF page 185')) return false;
    var ready = elapsed >= value.requiredMinutes
        && value.sleepMinutes >= policy.longRest.minimumSleepMinutes
        && value.lightActivityMinutes <= policy.longRest.maximumLightActivityMinutes;
    return (value.status === 'ready') === ready;
}

function initiativeRestInterruption() {
    if (!Object.prototype.hasOwnProperty.call(subject.components, EPISODE)) return null;
    var episode = parse(subject.components[EPISODE], 'Rest episode');
    var policyReference = ctx.references && ctx.references[episode.policyEntityId];
    var policy = policyReference && parse(policyReference.components[POLICY], 'Rest policy');
    if (!policyReference || policyReference.id !== STANDARD_POLICY || !validPolicy(policy)
        || !validEpisode(episode, policy)) {
        throw new Error('Initiative requires valid rest policy and episode state.');
    }
    var related = subject.related || [];
    var memberships = [];
    for (var relatedIndex = 0; relatedIndex < related.length; relatedIndex++) {
        var candidate = related[relatedIndex];
        if (candidate.kind === REST_WORLD
            && candidate.fromEntityId === candidate.id
            && candidate.toEntityId === subject.id) memberships.push(candidate);
    }
    if (memberships.length !== 1 || memberships[0].data !== '{}') {
        throw new Error('Rest episode requires exactly one matching world membership.');
    }
    var world = memberships[0];
    var root = parse(world.components[WORLD_ROOT], 'World root');
    var clock = parse(world.components[WORLD_CLOCK], 'World clock');
    if (!closed(root, ['status', 'summary', 'visibility'])
        || root.status !== 'active'
        || !validClock(clock)
        || episode.worldId !== world.id
        || clock.currentMinute !== episode.observedAtMinute
        || clock.revision !== episode.observedClockRevision) {
        throw new Error('Initiative rest context is invalid or stale.');
    }
    if (episode.status === 'ready') return null;
    var interruptions = episode.kind === 'short'
        ? policy.shortRest.interruptions
        : policy.longRest.interruptions;
    if (interruptions.indexOf('initiative') < 0) {
        throw new Error('Initiative is not a permitted rest interruption.');
    }
    if (episode.kind === 'short') {
        return { outcome: 'short-stopped', worldId: world.id, benefitsGranted: false };
    }
    if (episode.requiredMinutes > REST_MAX - policy.longRest.additionalMinutesPerInterruption
        || episode.interruptionCount >= 16666658) {
        throw new Error('Long Rest interruption exceeds the supported duration bounds.');
    }
    var next = {
        policyEntityId: episode.policyEntityId,
        kind: 'long',
        worldId: episode.worldId,
        startedAtMinute: episode.startedAtMinute,
        observedAtMinute: episode.observedAtMinute,
        observedClockRevision: episode.observedClockRevision,
        requiredMinutes: episode.requiredMinutes + policy.longRest.additionalMinutesPerInterruption,
        sleepMinutes: episode.sleepMinutes,
        lightActivityMinutes: episode.lightActivityMinutes,
        interruptionCount: episode.interruptionCount + 1,
        status: 'active',
        sourceRef: episode.sourceRef
    };
    return { outcome: 'long-resumed', worldId: world.id, episode: next, benefitsGranted: false };
}

function level() {
    var children = ctx.children && ctx.children.level;
    if (!Array.isArray(children)
        || children.length !== 1
        || !children[0].roleEntityIds
        || children[0].roleEntityIds.subject !== subject.id) {
        throw new Error('Exactly one matching character-level result is required.');
    }
    var value = parse(children[0].output && children[0].output.data, 'Character-level result');
    if (value.test !== 'character-level-read'
        || value.subjectId !== subject.id
        || value.present !== true
        || value.valid !== true
        || !Number.isSafeInteger(value.totalLevel)
        || value.totalLevel < 1
        || value.totalLevel > 20
        || !Number.isSafeInteger(value.proficiencyBonus)
        || value.proficiencyBonus < 2
        || value.proficiencyBonus > 6) {
        throw new Error('Character-level result is invalid.');
    }
    return value;
}

if (!subject || !object(input)) {
    throw new Error('Initiative requires one subject and object input.');
}

var inputKeys = Object.keys(input);
for (var inputIndex = 0; inputIndex < inputKeys.length; inputIndex++) {
    if (inputKeys[inputIndex] !== 'rollCircumstances'
        && inputKeys[inputIndex] !== 'useAlertInitiativeProficiency') {
        throw new Error('Initiative input contains an unsupported field.');
    }
}

var useAlert = Object.prototype.hasOwnProperty.call(input, 'useAlertInitiativeProficiency')
    ? input.useAlertInitiativeProficiency
    : false;
if (typeof useAlert !== 'boolean') {
    throw new Error('useAlertInitiativeProficiency must be Boolean.');
}

var abilities = parse(subject.components[ABILITIES], 'Ability scores');
if (!closed(abilities, ['scores'])
    || !object(abilities.scores)
    || !Number.isSafeInteger(abilities.scores[DEX])
    || abilities.scores[DEX] < 0
    || abilities.scores[DEX] > 100) {
    throw new Error('Dexterity score is invalid.');
}

var state = parse(subject.components[ENTITLEMENTS], 'Feature entitlements');
if (!closed(state, ['entitlements'])
    || !Array.isArray(state.entitlements)
    || state.entitlements.length > 1024) {
    throw new Error('Feature entitlements are invalid.');
}

var seen = {};
var alertEntitlement = null;
for (var entitlementIndex = 0; entitlementIndex < state.entitlements.length; entitlementIndex++) {
    var entitlement = state.entitlements[entitlementIndex];
    var commonValid = closed(
        entitlement,
        ['featureRef', 'grantedByRef', 'grantKind', 'sourceRef'],
        ['configurationKey', 'classLevel'])
        && entityRef(entitlement.featureRef)
        && entityRef(entitlement.grantedByRef)
        && sourceRef(entitlement.sourceRef);
    var originValid = entitlement.grantKind === 'origin-feat'
        && typeof entitlement.configurationKey === 'string'
        && /^[a-z0-9-]+$/.test(entitlement.configurationKey)
        && entitlement.configurationKey.length <= 80
        && !Object.prototype.hasOwnProperty.call(entitlement, 'classLevel');
    var classValid = entitlement.grantKind === 'class-feature'
        && Number.isSafeInteger(entitlement.classLevel)
        && entitlement.classLevel >= 1
        && entitlement.classLevel <= 20
        && !Object.prototype.hasOwnProperty.call(entitlement, 'configurationKey');
    if (!commonValid || (!originValid && !classValid)) {
        throw new Error('Feature entitlements are invalid.');
    }
    var identity = entitlement.featureRef.entityId
        + '\n' + entitlement.grantedByRef.entityId
        + '\n' + entitlement.grantKind
        + '\n' + (entitlement.configurationKey || entitlement.classLevel);
    if (seen[identity]) throw new Error('Feature entitlements are duplicated.');
    seen[identity] = true;
    if (entitlement.featureRef.entityId === ALERT) {
        if (alertEntitlement !== null) throw new Error('Alert entitlement is duplicated.');
        if (!originValid || entitlement.configurationKey !== 'default') {
            throw new Error('Alert entitlement is invalid.');
        }
        alertEntitlement = entitlement;
    }
}

var alertAvailable = alertEntitlement !== null;
if (useAlert && !alertAvailable) {
    throw new Error('Alert Initiative Proficiency requires an Alert entitlement.');
}

var proficiencyBonus = alertAvailable ? level().proficiencyBonus : 0;
var hasCircumstances = Object.prototype.hasOwnProperty.call(input, 'rollCircumstances');
var circumstances = hasCircumstances ? input.rollCircumstances : [];
if (!Array.isArray(circumstances) || (hasCircumstances && circumstances.length === 0)) {
    throw new Error('rollCircumstances must be a nonempty array when supplied.');
}

var advantage = false;
var disadvantage = false;
var pairs = {};
var accepted = [];
for (var circumstanceIndex = 0; circumstanceIndex < circumstances.length; circumstanceIndex++) {
    var circumstance = circumstances[circumstanceIndex];
    if (!closed(circumstance, ['kind', 'source'])
        || (circumstance.kind !== 'advantage' && circumstance.kind !== 'disadvantage')
        || typeof circumstance.source !== 'string'
        || circumstance.source.trim() !== circumstance.source
        || !circumstance.source.length
        || circumstance.source.indexOf('condition:') === 0) {
        throw new Error('Roll circumstance is invalid.');
    }
    var pair = circumstance.kind + '\n' + circumstance.source;
    if (pairs[pair]) throw new Error('Roll circumstance is duplicated.');
    pairs[pair] = true;
    accepted.push(circumstance);
    if (circumstance.kind === 'advantage') advantage = true;
    else disadvantage = true;
}

var rollMode = advantage && disadvantage
    ? 'normal'
    : advantage
        ? 'advantage'
        : disadvantage
            ? 'disadvantage'
            : 'normal';
var rolls = [ctx.randomInt(1, 20)];
if (rollMode !== 'normal') rolls.push(ctx.randomInt(1, 20));
var roll = rolls.length === 1
    ? rolls[0]
    : rollMode === 'advantage'
        ? Math.max(rolls[0], rolls[1])
        : Math.min(rolls[0], rolls[1]);
var dexterityScore = abilities.scores[DEX];
var dexterityModifier = Math.floor((dexterityScore - 10) / 2);
var alertBonus = useAlert ? proficiencyBonus : 0;
var initiative = roll + dexterityModifier + alertBonus;
var modifiers = [{ source: 'ability:dexterity', value: dexterityModifier }];
if (useAlert) modifiers.push({ source: 'feat:alert', value: alertBonus });
var restInterruption = initiativeRestInterruption();

return {
    narration: subject.name + ' rolls Initiative ' + initiative + '.',
    effects: [],
    events: [],
    notifications: [],
    data: {
        test: 'initiative',
        subjectId: subject.id,
        ability: 'dex',
        die: '1d20',
        rollMode: rollMode,
        rolls: rolls,
        roll: roll,
        rollCircumstances: accepted,
        modifiers: modifiers,
        initiative: initiative,
        source: 'dnd2024.source.srd-5.2.1',
        alertInitiativeProficiency: {
            available: alertAvailable,
            used: useAlert,
            bonus: alertAvailable ? proficiencyBonus : 0,
            featureRef: alertAvailable ? alertEntitlement.featureRef : null,
            sourceRef: alertAvailable ? alertEntitlement.sourceRef : null
        },
        restInterruption: restInterruption
    }
};
