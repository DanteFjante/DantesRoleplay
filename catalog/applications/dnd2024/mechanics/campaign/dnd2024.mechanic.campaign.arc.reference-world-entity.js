var c=ctx.roles.campaign,a=ctx.roles.arc,t=ctx.roles.target,ROOT='dnd2024.game.core.campaign.root',ARC='dnd2024.game.core.campaign.arc',HAS='dnd2024.game.core.campaign.has-arc',RELEVANT='dnd2024.game.core.campaign.references',LINK='dnd2024.game.core.campaign.record.references-world-entity';
function parse(v,n){try{var x=JSON.parse(v);if(!x||Array.isArray(x)||typeof x!=='object')throw 0;return x;}catch(e){throw new Error(n+' is invalid.');}}
function edge(r,f,to,k){var local=k.slice(8);return (r.relationships||[]).some(function(e){return e.fromEntityId===f&&e.toEntityId===to&&e.kind===local&&e.data==='{}';});}
if(!ctx.input||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==0)throw new Error('Arc reference input must be empty.');
var root=parse(c.components[ROOT],'Campaign'),arc=parse(a.components[ARC],'Arc');
if(root.status!=='active'||['resolved','abandoned'].indexOf(arc.status)<0||typeof arc.closingSummary!=='string'||arc.closingSummary.length<1)throw new Error('Campaign or terminal arc state is invalid.');
if(!edge(a,c.id,a.id,HAS)||!edge(c,c.id,t.id,RELEVANT))throw new Error('Arc ownership or target relevance is missing.');
if(edge(a,a.id,t.id,LINK))throw new Error('The arc already references this World entity.');
return {narration:a.name+' now references '+t.name+'.',effects:[{type:'relationship.create',entityId:a.id,toEntityId:t.id,kind:LINK,data:'{}'}],events:[],notifications:[],data:{test:'campaign-arc-world-reference',campaignId:c.id,arcId:a.id,targetId:t.id}};
