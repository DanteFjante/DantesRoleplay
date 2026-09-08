var c=ctx.roles&&ctx.roles.campaign,CAMPAIGN='game.core.campaign.root',WORLD='game.core.world.root',INWORLD='game.core.campaign.in-world';
function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function parse(raw,label){try{var v=JSON.parse(raw);if(!object(v))throw 0;return v;}catch(e){throw new Error(label+' is malformed.');}}
function text(v,max){return typeof v==='string'&&v.trim()===v&&v.length>0&&v.length<=max;}
if(!c||!object(ctx.input)||Object.keys(ctx.input).length)throw new Error('Campaign context requires one campaign and empty input.');
var root=parse(c.components&&c.components[CAMPAIGN],'Campaign root');
if(root.status!=='active'||root.rulesetScope!=='dnd2024'||!text(c.id,200)||!text(c.name,400))throw new Error('Campaign context requires an active D&D campaign.');
var worlds=(c.related||[]).filter(function(v){return v.kind===INWORLD&&v.fromEntityId===c.id&&v.toEntityId===v.id;});
if(worlds.length!==1)throw new Error('Campaign context requires one exact declared World.');
var w=worlds[0],world=parse(w.components&&w.components[WORLD],'World root');
if(world.status!=='active'||!text(w.id,200)||!text(w.name,400))throw new Error('Campaign context requires an active declared World.');
return {narration:'Projected the campaign and its declared World context.',effects:[],events:[],notifications:[],data:{version:1,campaignId:c.id,worldId:w.id,campaign:{id:c.id,name:c.name},world:{id:w.id,name:w.name}}};
