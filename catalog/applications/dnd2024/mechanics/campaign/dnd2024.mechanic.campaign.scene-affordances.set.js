var c=ctx.roles.campaign,i=ctx.input,ROOT='game.core.campaign.root',SCENE='game.core.campaign.current-scene',AFF='game.core.campaign.scene-affordances';
function exact(v,keys){if(!v||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function text(v,max){return typeof v==='string'&&v.trim().length>0&&v.length<=max;}
if(!c||!c.components||!c.components[ROOT]||!c.components[SCENE])throw new Error('Scene affordances require a campaign with a current scene.');
if(!exact(i,['items'])||!Array.isArray(i.items)||i.items.length>24)throw new Error('Scene affordance input requires exactly a bounded items array.');
var seen={};i.items.forEach(function(x){if(!exact(x,['key','label','summary','visibility'])||!(/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/.test(x.key))||x.key.length>64||!text(x.label,120)||!text(x.summary,500)||(x.visibility!=='party'&&x.visibility!=='gm')||seen[x.key])throw new Error('Every affordance must be unique and follow the closed presentation contract.');seen[x.key]=true;});
var scene=JSON.parse(c.components[SCENE]),value={scene:scene,items:i.items},prior=c.components[AFF]||null;
return {narration:'Available scene actions for '+c.name+' are updated.',effects:[{type:prior?'component.set':'component.add',entityId:c.id,definitionId:AFF,data:JSON.stringify(value)}],events:[],notifications:[],data:{campaignId:c.id,itemCount:i.items.length}};
