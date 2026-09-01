var c=ctx.roles.campaign,w=ctx.roles.world,l=ctx.roles.location,i=ctx.input,ROOT='game.core.campaign.root',WORLD='game.core.world.root',LOCATION='game.core.world.location',SCENE='game.core.campaign.current-scene',INWORLD='game.core.campaign.in-world',REF='game.core.campaign.references';
function exact(v,keys){if(!v||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function edge(e,from,to,kind){return (e.relationships||[]).some(function(x){return x.fromEntityId===from&&x.toEntityId===to&&(x.kind===kind||x.kind==='dnd2024.'+kind);});}
function contains(nodes,id){return (nodes||[]).some(function(x){return x.id===id||contains(x.contains,id);});}
if(!c||!w||!l||!c.components||!w.components||!l.components||!c.components[ROOT]||!w.components[WORLD]||!l.components[LOCATION])throw new Error('Current scene requires one campaign, its World, and one existing location.');
if(!exact(i,['mode'])||(i.mode!=='record'&&i.mode!=='move'))throw new Error('Current-scene input requires exactly mode record or move.');
if(!edge(c,c.id,w.id,INWORLD)||(!contains(w.contains,l.id)&&!edge(l,l.id,w.id,'game.core.world.location.in-world')))throw new Error('The destination is outside the campaign World.');
var prior=c.components[SCENE]||null;if(i.mode==='record'&&prior!==null)throw new Error('Current scene is already recorded.');if(i.mode==='move'&&prior===null)throw new Error('Current scene is absent.');
var effects=[];if(!edge(c,c.id,l.id,REF))effects.push({type:'relationship.create',entityId:c.id,toEntityId:l.id,kind:REF,data:JSON.stringify({role:'current-location',audience:'party'})});
effects.push({type:prior?'component.set':'component.add',entityId:c.id,definitionId:SCENE,data:JSON.stringify({location:{entityId:l.id}})});
return {narration:c.name+' is now at '+l.name+'.',effects:effects,events:[],notifications:[],data:{campaignId:c.id,locationId:l.id,mode:i.mode}};
