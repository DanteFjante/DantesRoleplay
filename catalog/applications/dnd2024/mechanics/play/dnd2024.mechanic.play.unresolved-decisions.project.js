var STATE='dnd2024.play.scene-state',DECISION='dnd2024.play.decision-point';
function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function parse(raw,label){try{var v=JSON.parse(raw);if(!object(v))throw 0;return v;}catch(e){throw new Error(label+' is malformed.');}}
function read(e,id,label,optional){var raw=e.components&&e.components[id];if(typeof raw!=='string'){if(optional)return null;throw new Error(label+' is missing.');}return parse(raw,label);}
function ref(v){return object(v)&&typeof v.entityId==='string'&&v.entityId.length>0?v.entityId:null;}
if(!ctx.roles||!ctx.roles.scene||!object(ctx.input)||Object.keys(ctx.input).length)throw new Error('Unresolved-decisions projection requires one scene and empty input.');
var s=ctx.roles.scene,state=read(s,STATE,'Scene state',false),decision=read(s,DECISION,'Decision point',true),items=[];
if(!ref(state.scene)||typeof state.status!=='string')throw new Error('Scene state is malformed.');
if(state.currentDecisionPoint&&!decision)throw new Error('Scene names a decision point without authoritative decision state.');
if(decision&&decision.status!=='resolved'&&decision.status!=='cancelled'){
 if(!Array.isArray(decision.eligibleParticipants)||!Array.isArray(decision.declaredIntents))throw new Error('Decision point is malformed.');
 items.push({sceneId:s.id,status:decision.status,promptingEntityId:ref(decision.promptingEntity||{}),eligibleParticipantIds:decision.eligibleParticipants.map(ref),declaredIntents:decision.declaredIntents.map(function(v){return {participantId:ref(v.participant),actorId:ref(v.actor||{}),text:v.text,activityId:ref(v.activity||{})};})});
}
return {narration:items.length?'Projected one unresolved decision point.':'No unresolved decision point is recorded on this scene.',effects:[],events:[],notifications:[],data:{version:1,scene:{id:s.id,name:s.name,status:state.status,pillarId:ref(state.pillar||{}),encounterId:ref(state.encounter||{})},items:items}};

