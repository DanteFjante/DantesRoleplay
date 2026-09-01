var c=ctx.roles.campaign,s=ctx.roles.session,t=ctx.roles.target,ROOT='game.core.campaign.root',SESSION='game.core.campaign.session',RECAP='game.core.campaign.session-recap',HAS='game.core.campaign.has-session',RELEVANT='game.core.campaign.references',LINK='game.core.campaign.record.references-world-entity';
function parse(v,n){try{var x=JSON.parse(v);if(!x||Array.isArray(x)||typeof x!=='object')throw 0;return x;}catch(e){throw new Error(n+' is invalid.');}}
function edge(r,f,to,k){return (r.relationships||[]).some(function(e){return e.fromEntityId===f&&e.toEntityId===to&&(e.kind===k||e.kind==='dnd2024.'+k)&&e.data==='{}';});}
if(!ctx.input||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==0)throw new Error('Session reference input must be empty.');
var root=parse(c.components[ROOT],'Campaign'),session=parse(s.components[SESSION],'Session'),recap=parse(s.components[RECAP],'Session recap');
if(root.status!=='active'||session.status!=='ended'||!Number.isSafeInteger(session.ordinal)||session.ordinal<1||recap.protocolVersion!=='session.s0.c3-only.v1')throw new Error('Campaign or ended session state is invalid.');
if(!edge(s,c.id,s.id,HAS)||!edge(c,c.id,t.id,RELEVANT))throw new Error('Session ownership or target relevance is missing.');
if(edge(s,s.id,t.id,LINK))throw new Error('The session already references this World entity.');
return {narration:s.name+' now references '+t.name+'.',effects:[{type:'relationship.create',entityId:s.id,toEntityId:t.id,kind:LINK,data:'{}'}],events:[],notifications:[],data:{test:'campaign-session-world-reference',campaignId:c.id,sessionId:s.id,targetId:t.id}};
