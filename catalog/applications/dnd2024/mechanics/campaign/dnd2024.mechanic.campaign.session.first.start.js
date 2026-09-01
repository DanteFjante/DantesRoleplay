var c=ctx.roles.campaign,i=ctx.input,ROOT='game.core.campaign.root',SESSION='game.core.campaign.session',HAS='game.core.campaign.has-session';
function exact(v,keys){if(!v||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function any(){return (c.relationships||[]).some(function(x){return x.fromEntityId===c.id&&(x.kind===HAS||x.kind==='dnd2024.'+HAS);});}
if(!c||!c.components||!c.components[ROOT])throw new Error('First-session start requires an active campaign root.');
if(!exact(i,['sessionId'])||!/^session\.[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(i.sessionId))throw new Error('First-session input requires exactly one canonical sessionId.');
if(any())throw new Error('This first-session mechanic cannot run after any session has been retained.');
return {narration:'The first session of '+c.name+' begins.',effects:[{type:'entity.create',entityId:i.sessionId,name:c.name+' session 1'},{type:'component.add',entityId:i.sessionId,definitionId:SESSION,data:JSON.stringify({status:'active',ordinal:1})},{type:'relationship.create',entityId:c.id,toEntityId:i.sessionId,kind:HAS,data:'{}'}],events:[],notifications:[],data:{campaignId:c.id,sessionId:i.sessionId,ordinal:1,status:'active'}};
