var ROOT='game.core.campaign.root',SCENE='game.core.campaign.current-scene',AFF='game.core.campaign.scene-affordances',LOCATION='game.core.world.location';
function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function parse(raw,label){try{var v=JSON.parse(raw);if(!object(v))throw 0;return v;}catch(e){throw new Error(label+' is malformed.');}}
function read(e,id,label,optional){var raw=e.components&&e.components[id];if(typeof raw!=='string'){if(optional)return null;throw new Error(label+' is missing.');}return parse(raw,label);}
function ref(v){return object(v)&&Object.keys(v).length===1&&typeof v.entityId==='string'&&v.entityId.length>0?v.entityId:null;}
function same(a,b){return ref(a.location)===ref(b.location)&&ref(a.conversation||{})===ref(b.conversation||{})&&ref(a.encounter||{})===ref(b.encounter||{});}
if(!ctx.roles||!ctx.roles.campaign||!object(ctx.input)||Object.keys(ctx.input).length)throw new Error('Current-scene projection requires one campaign and empty input.');
var c=ctx.roles.campaign,root=read(c,ROOT,'Campaign root',false),scene=read(c,SCENE,'Current scene',false),locationId=ref(scene.location),location=locationId&&ctx.references&&ctx.references[locationId],locationState=location&&read(location,LOCATION,'Current location',false);
if(root.status!=='active'||!locationId||!location||!locationState||locationState.status!=='active')throw new Error('Current scene is not complete and active.');
if(locationState.visibility==='gm')throw new Error('Current scene is not party-visible.');
var affordances=[],aff=read(c,AFF,'Scene affordances',true);if(aff){if(!same(scene,aff.scene)||!Array.isArray(aff.items))throw new Error('Scene affordances are stale or malformed.');affordances=aff.items.filter(function(v){return v.visibility==='party';}).map(function(v){return {key:v.key,label:v.label,summary:v.summary};});}
var kind=ref(scene.encounter||{})?'combat':(ref(scene.conversation||{})?'conversation':'exploration');
return {narration:'Projected the exact party-visible current scene.',effects:[],events:[],notifications:[],data:{version:1,kind:kind,location:{id:location.id,kind:locationState.kind,summary:locationState.summary,visibility:locationState.visibility},conversationId:ref(scene.conversation||{}),encounterId:ref(scene.encounter||{}),affordances:affordances}};
