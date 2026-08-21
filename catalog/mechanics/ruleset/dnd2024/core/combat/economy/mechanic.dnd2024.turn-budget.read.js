var SID='source.dnd2024.srd-5.2.1',LOC='Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn',DEF='dnd2024.turn-budget',p=ctx.roles.participant,i=ctx.input;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function integer(v,min,max){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=max;}
function source(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['action','bonusAction','reaction','freeInteraction','movementRemainingFeet','sourceRef'])&&typeof v.action==='boolean'&&typeof v.bonusAction==='boolean'&&typeof v.reaction==='boolean'&&typeof v.freeInteraction==='boolean'&&integer(v.movementRemainingFeet,0,1000)&&source(v.sourceRef);}
if(!p)throw new Error('A participant role is required.');
if(!closed(i,[]))throw new Error('Reading turn budget diagnostics requires exactly an empty object input.');
var raw=p.components&&p.components[DEF],state;
if(!raw)return {narration:p.name+' has no turn budget.',data:{test:'turn-budget-read',participantId:p.id,present:false,valid:false,problem:'absent',budget:null},effects:[]};
try{state=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){return {narration:p.name+' has a malformed turn budget.',data:{test:'turn-budget-read',participantId:p.id,present:true,valid:false,problem:'malformed',budget:null},effects:[]};}
if(!valid(state))return {narration:p.name+' has an invalid turn budget.',data:{test:'turn-budget-read',participantId:p.id,present:true,valid:false,problem:'invalid',budget:null},effects:[]};
return {narration:p.name+' has a valid turn budget.',data:{test:'turn-budget-read',participantId:p.id,present:true,valid:true,problem:null,budget:state},effects:[]};
