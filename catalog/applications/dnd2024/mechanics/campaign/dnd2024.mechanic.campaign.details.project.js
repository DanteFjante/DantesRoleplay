var c=ctx.roles&&ctx.roles.campaign,C={root:'game.core.campaign.root',chapter:'game.core.campaign.chapter',arc:'game.core.campaign.arc',session:'game.core.campaign.session',recap:'game.core.campaign.session-recap'},K={chapter:'game.core.campaign.has-chapter',arc:'game.core.campaign.has-arc',session:'game.core.campaign.has-session'};
function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function parse(raw,label,optional){if(typeof raw!=='string'){if(optional)return null;throw new Error(label+' is missing.');}try{var v=JSON.parse(raw);if(!object(v))throw 0;return v;}catch(e){throw new Error(label+' is malformed.');}}
function text(v,max){return typeof v==='string'&&v.trim()===v&&v.length>0&&v.length<=max;}
function named(v){if(!text(v.id,200)||!text(v.name,400))throw new Error('Campaign structure identity is invalid.');return {id:v.id,name:v.name};}
if(!c||!object(ctx.input)||Object.keys(ctx.input).length)throw new Error('Campaign details requires one campaign and empty input.');
var root=parse(c.components&&c.components[C.root],'Campaign root');if(root.status!=='active'||root.rulesetScope!=='dnd2024')throw new Error('Campaign details requires an active D&D campaign.');
var dm=ctx.audience&&ctx.audience.perspective==='dm',chapters=[],arcs=[],sessions=[];
for(var i=0;i<(c.related||[]).length;i++){var r=c.related[i],base=named(r),v,out;
 if(r.kind===K.chapter){v=parse(r.components&&r.components[C.chapter],'Campaign chapter');out={id:base.id,name:base.name,status:v.status,title:v.title,partyQuestion:v.partyQuestion};if(v.closingSummary)out.closingSummary=v.closingSummary;if(dm&&v.gmContext)out.gmContext=v.gmContext;chapters.push(out);}
 else if(r.kind===K.arc){v=parse(r.components&&r.components[C.arc],'Campaign arc');out={id:base.id,name:base.name,status:v.status,title:v.title,partyStake:v.partyStake};if(v.closingSummary)out.closingSummary=v.closingSummary;if(dm&&v.gmContext)out.gmContext=v.gmContext;arcs.push(out);}
 else if(r.kind===K.session&&dm){v=parse(r.components&&r.components[C.session],'Campaign session');var recap=parse(r.components&&r.components[C.recap],'Campaign session recap',true);if(v.status==='ended'&&!recap)throw new Error('Ended campaign session is missing its recap.');out={id:base.id,name:base.name,status:v.status,ordinal:v.ordinal};if(recap)out.recap=recap;sessions.push(out);}
}
chapters.sort(function(a,b){return a.name.localeCompare(b.name)||a.id.localeCompare(b.id);});arcs.sort(function(a,b){return a.name.localeCompare(b.name)||a.id.localeCompare(b.id);});sessions.sort(function(a,b){return a.ordinal-b.ordinal||a.id.localeCompare(b.id);});
return {narration:'Projected the complete bounded campaign structure.',effects:[],events:[],notifications:[],data:{version:1,campaignId:c.id,chapters:chapters,arcs:arcs,sessions:sessions}};
