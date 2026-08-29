var scenarioRole=ctx.roles.scenario, scenarioId='trail-survival.scenario';
function closed(v,keys){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();if(a.length!==b.length)return false;for(var i=0;i<b.length;i++)if(a[i]!==b[i])return false;return true;}
function parse(v,name){if(typeof v!=='string')throw new Error(name+' is corrupt.');try{return JSON.parse(v);}catch(e){throw new Error(name+' is corrupt.');}}
function integer(v,min,max){return typeof v==='number'&&Number.isSafeInteger(v)&&v>=min&&v<=max;}
function text(v,max){return typeof v==='string'&&v.length>0&&v.trim()===v&&Array.from(v).length<=max;}
function unique(values,name){var seen={};for(var i=0;i<values.length;i++){var key=values[i];if(seen[key])throw new Error(name+' contains duplicate id '+key+'.');seen[key]=true;}return seen;}
function add(effects,type,entityId,definitionId,value){effects.push({type:type,entityId:entityId,definitionId:definitionId,data:JSON.stringify(value)});}
if(!scenarioRole||!scenarioRole.components||!scenarioRole.components[scenarioId])throw new Error('Run creation requires one scenario role.');
if(!closed(ctx.input,['conveyanceId','members','partyId','partyName','runId']))throw new Error('Run creation input is not the closed setup shape.');
if(!text(ctx.input.runId,200)||!text(ctx.input.partyId,200)||!text(ctx.input.partyName,200)||!text(ctx.input.conveyanceId,200))throw new Error('Run, party, conveyance, and party-name values must be non-empty bounded text.');
if(!Array.isArray(ctx.input.members)||ctx.input.members.length<1||ctx.input.members.length>32)throw new Error('Run creation requires one to 32 members.');
var entityIds=[scenarioRole.id,ctx.input.runId,ctx.input.partyId,ctx.input.conveyanceId], memberIds=[];
for(var mi=0;mi<ctx.input.members.length;mi++){
  var member=ctx.input.members[mi];
  if(!closed(member,['entityId','name','roleId'])||!text(member.entityId,200)||!text(member.name,200)||!text(member.roleId,100))throw new Error('Each member requires exactly a bounded entityId, name, and roleId.');
  entityIds.push(member.entityId);memberIds.push(member.entityId);
}
unique(entityIds,'Setup entity identities');
if(!integer(ctx.seed,1,4294967295))throw new Error('Run creation requires a non-zero unsigned 32-bit host seed.');
var s=parse(scenarioRole.components[scenarioId],'Scenario');
var scenarioKeys=['conveyance','currencyResourceId','defaultPolicy','events','finalLandmarkId','foodResourceId','forage','initialResources','market','memberMaxHealth','outcomes','paces','rations','rest','routeId','routeLegs','rulesProfileId','scenarioContentHash','scenarioId','scenarioVersion','startLandmarkId'];
if(!closed(s,scenarioKeys)||s.scenarioId!==scenarioRole.id||!text(s.scenarioId,100)||!integer(s.scenarioVersion,1,2147483647)||typeof s.scenarioContentHash!=='string'||!/^[0-9A-F]{64}$/.test(s.scenarioContentHash)||!text(s.rulesProfileId,100)||!text(s.routeId,100)||!text(s.startLandmarkId,100)||!text(s.finalLandmarkId,100)||s.startLandmarkId===s.finalLandmarkId||!text(s.currencyResourceId,100)||!text(s.foodResourceId,100)||s.currencyResourceId===s.foodResourceId||!integer(s.memberMaxHealth,1,1000000))throw new Error('Scenario identity or root tuning is invalid.');
if(!closed(s.conveyance,['cargoCapacity','condition','kindId','maximumCondition'])||!text(s.conveyance.kindId,100)||!integer(s.conveyance.condition,1,1000000000)||!integer(s.conveyance.maximumCondition,1,1000000000)||s.conveyance.condition>s.conveyance.maximumCondition||!integer(s.conveyance.cargoCapacity,0,1000000000))throw new Error('Scenario conveyance is invalid.');
if(!closed(s.defaultPolicy,['paceId','rationId'])||!text(s.defaultPolicy.paceId,100)||!text(s.defaultPolicy.rationId,100))throw new Error('Scenario default policy is invalid.');
if(!Array.isArray(s.initialResources)||s.initialResources.length<1||s.initialResources.length>128)throw new Error('Scenario initial resources are invalid.');
var resourceIds=[];
for(var ri=0;ri<s.initialResources.length;ri++){var resource=s.initialResources[ri];if(!closed(resource,['quantity','resourceId'])||!text(resource.resourceId,100)||!integer(resource.quantity,0,1000000000))throw new Error('Scenario initial resource is invalid.');resourceIds.push(resource.resourceId);}
var resources=unique(resourceIds,'Scenario resources');
if(!resources[s.currencyResourceId]||!resources[s.foodResourceId])throw new Error('Scenario currency and food must be initial resources.');
if(!Array.isArray(s.paces)||s.paces.length<1||s.paces.length>32)throw new Error('Scenario paces are invalid.');
var paceIds=[];
for(var pi=0;pi<s.paces.length;pi++){var pace=s.paces[pi];if(!closed(pace,['conveyanceWear','distancePerTurn','eventChancePer10000','minutesPerTurn','paceId'])||!text(pace.paceId,100)||!integer(pace.distancePerTurn,1,1000000000)||!integer(pace.minutesPerTurn,1,1000000000)||!integer(pace.conveyanceWear,0,1000000000)||!integer(pace.eventChancePer10000,0,10000))throw new Error('Scenario pace is invalid.');paceIds.push(pace.paceId);}
var paces=unique(paceIds,'Scenario paces');
if(!paces[s.defaultPolicy.paceId])throw new Error('Scenario default pace is missing.');
if(!Array.isArray(s.rations)||s.rations.length<1||s.rations.length>32)throw new Error('Scenario rations are invalid.');
var rationIds=[];
for(var qi=0;qi<s.rations.length;qi++){var ration=s.rations[qi];if(!closed(ration,['foodPerMember','healthDelta','rationId'])||!text(ration.rationId,100)||!integer(ration.foodPerMember,0,1000000000)||!integer(ration.healthDelta,-1000000,1000000))throw new Error('Scenario ration is invalid.');rationIds.push(ration.rationId);}
var rations=unique(rationIds,'Scenario rations');
if(!rations[s.defaultPolicy.rationId])throw new Error('Scenario default ration is missing.');
if(!closed(s.market,['landmarkIds','offers'])||!Array.isArray(s.market.landmarkIds)||s.market.landmarkIds.length<1||s.market.landmarkIds.length>1000||!Array.isArray(s.market.offers)||s.market.offers.length<1||s.market.offers.length>128)throw new Error('Scenario market is invalid.');
unique(s.market.landmarkIds,'Scenario market landmarks');
var offerIds=[];
for(var oi=0;oi<s.market.offers.length;oi++){var offer=s.market.offers[oi];if(!closed(offer,['buyPrice','resourceId','sellPrice','unitWeight'])||!text(offer.resourceId,100)||!integer(offer.buyPrice,0,1000000000)||!integer(offer.sellPrice,0,1000000000)||offer.sellPrice>offer.buyPrice||!integer(offer.unitWeight,0,1000000000))throw new Error('Scenario market offer is invalid.');offerIds.push(offer.resourceId);}
var offers=unique(offerIds,'Scenario market offers');for(var ir=0;ir<resourceIds.length;ir++)if(!offers[resourceIds[ir]])throw new Error('Scenario lacks a market weight for an initial resource.');
if(!closed(s.rest,['foodPerMember','healthGain','minutes'])||!integer(s.rest.minutes,1,1000000000)||!integer(s.rest.foodPerMember,0,1000000000)||!integer(s.rest.healthGain,0,1000000))throw new Error('Scenario rest tuning is invalid.');
if(!closed(s.forage,['maximumYield','minimumYield','minutes'])||!integer(s.forage.minutes,1,1000000000)||!integer(s.forage.minimumYield,0,1000000000)||!integer(s.forage.maximumYield,0,1000000000)||s.forage.minimumYield>s.forage.maximumYield)throw new Error('Scenario forage tuning is invalid.');
if(!Array.isArray(s.routeLegs)||s.routeLegs.length<1||s.routeLegs.length>1000)throw new Error('Scenario route is invalid.');
var legIds=[], landmarks={};landmarks[s.startLandmarkId]=true;
for(var li=0;li<s.routeLegs.length;li++){var leg=s.routeLegs[li];if(!closed(leg,['distance','fromLandmarkId','legId','toLandmarkId'])||!text(leg.legId,100)||!text(leg.fromLandmarkId,100)||!text(leg.toLandmarkId,100)||leg.fromLandmarkId===leg.toLandmarkId||!integer(leg.distance,1,1000000000))throw new Error('Scenario route leg is invalid.');legIds.push(leg.legId);landmarks[leg.fromLandmarkId]=true;landmarks[leg.toLandmarkId]=true;}
unique(legIds,'Scenario route legs');
if(!landmarks[s.finalLandmarkId])throw new Error('Scenario final landmark is absent from the route.');
for(var ml=0;ml<s.market.landmarkIds.length;ml++)if(!landmarks[s.market.landmarkIds[ml]])throw new Error('Scenario market references an unknown landmark.');
if(!Array.isArray(s.events)||s.events.length>128)throw new Error('Scenario events are invalid.');
var eventIds=[];
for(var ei=0;ei<s.events.length;ei++){var event=s.events[ei];if(!closed(event,['choices','eventId','weight'])||!text(event.eventId,100)||!integer(event.weight,1,1000000000)||!Array.isArray(event.choices)||event.choices.length<1||event.choices.length>16)throw new Error('Scenario event is invalid.');eventIds.push(event.eventId);var choiceIds=[];for(var ci=0;ci<event.choices.length;ci++){var choice=event.choices[ci];if(!closed(choice,['choiceId','conveyanceDelta','elapsedMinutes','healthDelta','outcomeCauseId','outcomeKind','resourceDeltas'])||!text(choice.choiceId,100)||!integer(choice.healthDelta,-1000000,1000000)||!integer(choice.conveyanceDelta,-1000000000,1000000000)||!integer(choice.elapsedMinutes,0,1000000000)||['none','victory','defeat'].indexOf(choice.outcomeKind)<0||((choice.outcomeKind==='none')!==(choice.outcomeCauseId===null))||!Array.isArray(choice.resourceDeltas)||choice.resourceDeltas.length>128)throw new Error('Scenario event choice is invalid.');choiceIds.push(choice.choiceId);var deltaIds=[];for(var di=0;di<choice.resourceDeltas.length;di++){var delta=choice.resourceDeltas[di];if(!closed(delta,['quantity','resourceId'])||!text(delta.resourceId,100)||!integer(delta.quantity,-1000000000,1000000000)||!offers[delta.resourceId])throw new Error('Scenario event resource delta is invalid.');deltaIds.push(delta.resourceId);}unique(deltaIds,'Scenario event resource deltas');}unique(choiceIds,'Scenario event choices');}
unique(eventIds,'Scenario events');
if(!closed(s.outcomes,['conveyanceDefeatCauseId','partyDefeatCauseId','victoryCauseId'])||!text(s.outcomes.victoryCauseId,100)||!text(s.outcomes.partyDefeatCauseId,100)||!text(s.outcomes.conveyanceDefeatCauseId,100))throw new Error('Scenario outcomes are invalid.');
var effects=[];
effects.push({type:'entity.create',entityId:ctx.input.runId,name:ctx.input.partyName+' Run'});
effects.push({type:'entity.create',entityId:ctx.input.partyId,name:ctx.input.partyName});
effects.push({type:'entity.create',entityId:ctx.input.conveyanceId,name:s.conveyance.kindId});
for(var m=0;m<ctx.input.members.length;m++)effects.push({type:'entity.create',entityId:ctx.input.members[m].entityId,name:ctx.input.members[m].name});
add(effects,'component.add',ctx.input.runId,'trail-survival.scenario-pin',{scenarioId:s.scenarioId,scenarioVersion:s.scenarioVersion,scenarioContentHash:s.scenarioContentHash,rulesProfileId:s.rulesProfileId});
add(effects,'component.add',ctx.input.runId,'trail-survival.run',{phase:'travel',turn:0,partyId:ctx.input.partyId,randomSeed:ctx.seed,seedCursor:0});
add(effects,'component.add',ctx.input.runId,'trail-survival.clock',{elapsedMinutes:0});
add(effects,'component.add',ctx.input.runId,'trail-survival.route-progress',{routeId:s.routeId,currentLandmarkId:s.startLandmarkId,activeLegId:null,distanceIntoLeg:0,visitedLandmarkIds:[s.startLandmarkId]});
add(effects,'component.add',ctx.input.runId,'trail-survival.policy',{paceId:s.defaultPolicy.paceId,rationId:s.defaultPolicy.rationId});
add(effects,'component.add',ctx.input.partyId,'trail-survival.party',{name:ctx.input.partyName,memberIds:memberIds,conveyanceId:ctx.input.conveyanceId});
add(effects,'component.add',ctx.input.partyId,'trail-survival.resources',{entries:s.initialResources});
add(effects,'component.add',ctx.input.conveyanceId,'trail-survival.conveyance',{kindId:s.conveyance.kindId,status:'operational',condition:s.conveyance.condition,maximumCondition:s.conveyance.maximumCondition,cargoCapacity:s.conveyance.cargoCapacity});
for(var n=0;n<ctx.input.members.length;n++)add(effects,'component.add',ctx.input.members[n].entityId,'trail-survival.member',{name:ctx.input.members[n].name,roleId:ctx.input.members[n].roleId,status:'active',healthPoints:s.memberMaxHealth,conditionIds:[]});
effects.push({type:'containment.move',entityId:ctx.input.partyId,toEntityId:ctx.input.runId,slot:'party'});
effects.push({type:'containment.move',entityId:ctx.input.conveyanceId,toEntityId:ctx.input.partyId,slot:'conveyance'});
for(var c=0;c<ctx.input.members.length;c++)effects.push({type:'containment.move',entityId:ctx.input.members[c].entityId,toEntityId:ctx.input.partyId,slot:'member'});
return {narration:ctx.input.partyName+' begins the journey.',effects:effects,events:[],data:{command:'run.create',runId:ctx.input.runId,scenarioId:s.scenarioId,turn:0}};
