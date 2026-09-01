var c=ctx.roles.campaign,w=ctx.roles.world,l=ctx.roles.location,i=ctx.input;
var CAMPAIGN='game.core.campaign.root',SCENE='game.core.campaign.current-scene',WORLD='game.core.world.root',LOCATION='game.core.world.location',TRAVELLER='game.core.world.traveller',MOTIVE='game.core.world.motive',PARTICIPATION='game.core.campaign.character-participation';
var INWORLD='game.core.campaign.in-world',HAS='game.core.campaign.has-character-participation',FOR='game.core.campaign.character-participation.for-actor',REF='game.core.campaign.references';
function exact(v,keys){if(!v||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function text(v,max){return typeof v==='string'&&v.trim()===v&&v.length>0&&v.length<=max;}
function parse(raw,label){try{var value=JSON.parse(raw);if(!value||Array.isArray(value)||typeof value!=='object')throw 0;return value;}catch(error){throw new Error(label+' is malformed.');}}
function edge(e,from,to,kind){return (e.relationships||[]).some(function(x){return x.fromEntityId===from&&x.toEntityId===to&&(x.kind===kind||x.kind==='dnd2024.'+kind);});}
function contains(nodes,id){return (nodes||[]).some(function(x){return x.id===id||contains(x.contains,id);});}
if(!c||!w||!l||!c.components||!w.components||!l.components||!c.components[CAMPAIGN]||!c.components[SCENE]||!w.components[WORLD]||!l.components[LOCATION])throw new Error('Companion creation requires an active campaign, its World, and its exact current location.');
if(!exact(i,['actorId','name','motiveSummary'])||!/^actor\.[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(i.actorId)||!text(i.name,160)||!text(i.motiveSummary,1000))throw new Error('Companion input requires exactly actorId, name, and motiveSummary.');
var scene=parse(c.components[SCENE],'The campaign current scene');
if(!exact(scene,['location'])||!exact(scene.location,['entityId'])||scene.location.entityId!==l.id)throw new Error('The selected location is not the campaign current scene.');
if(!contains(w.contains,l.id)&&!edge(l,l.id,w.id,'game.core.world.location.in-world'))throw new Error('The selected location is not in the selected World.');
if(!edge(c,c.id,w.id,INWORLD))throw new Error('The campaign is not scoped to the selected World.');
if(edge(c,c.id,i.actorId,REF))throw new Error('The actor is already referenced by this campaign.');
var participationId=c.id+'.participation.'+i.actorId;
if(participationId.length>200)throw new Error('The derived campaign participation ID is too long.');
return {narration:i.name+' joins '+c.name+' at '+l.name+'.',effects:[
 {type:'entity.create',entityId:i.actorId,name:i.name},
 {type:'component.add',entityId:i.actorId,definitionId:TRAVELLER,data:'{"status":"active"}'},
 {type:'component.add',entityId:i.actorId,definitionId:MOTIVE,data:JSON.stringify({status:'active',summary:i.motiveSummary,visibility:'party'})},
 {type:'containment.move',entityId:i.actorId,toEntityId:l.id,slot:'presence'},
 {type:'entity.create',entityId:participationId,name:i.name+' campaign participation'},
 {type:'component.add',entityId:participationId,definitionId:PARTICIPATION,data:'{"status":"active"}'},
 {type:'relationship.create',entityId:c.id,toEntityId:participationId,kind:HAS,data:'{}'},
 {type:'relationship.create',entityId:participationId,toEntityId:i.actorId,kind:FOR,data:'{}'},
 {type:'relationship.create',entityId:c.id,toEntityId:i.actorId,kind:REF,data:'{"role":"companion","audience":"party"}'}
],events:[],notifications:[],data:{campaignId:c.id,worldId:w.id,locationId:l.id,actorId:i.actorId,participationId:participationId,partyModel:'derived-active-character-participations',characterSheetStatus:'not-created'}};
