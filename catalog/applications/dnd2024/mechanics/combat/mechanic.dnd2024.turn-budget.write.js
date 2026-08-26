var SID='source.dnd2024.srd-5.2.1',LOC='Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn',DEF='dnd2024.turn-budget',s=ctx.roles.subject,i=ctx.input;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function integer(v,min,max){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=max;}
function source(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['action','bonusAction','reaction','freeInteraction','movementRemainingFeet','sourceRef'])&&typeof v.action==='boolean'&&typeof v.bonusAction==='boolean'&&typeof v.reaction==='boolean'&&typeof v.freeInteraction==='boolean'&&integer(v.movementRemainingFeet,0,1000)&&source(v.sourceRef);}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,['mode','action','bonusAction','reaction','freeInteraction','movementRemainingFeet']))throw new Error('Input must contain exactly mode, four availability Booleans, and movementRemainingFeet. Do not supply a movement maximum, Speed, sourceRef, or effects.');
if(i.mode!=='record'&&i.mode!=='correct')throw new Error('mode must be exactly record or correct.');
var next={action:i.action,bonusAction:i.bonusAction,reaction:i.reaction,freeInteraction:i.freeInteraction,movementRemainingFeet:i.movementRemainingFeet,sourceRef:{sourceId:SID,locator:LOC}};
if(!valid(next))throw new Error('Turn-budget availability fields are Boolean and movementRemainingFeet is an integer from 0 through 1000.');
var raw=s.components&&s.components[DEF],previous=null;
if(raw){try{previous=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Existing turn budget is corrupt and cannot be corrected by this rule.');}if(!valid(previous))throw new Error('Existing turn budget is invalid and cannot be corrected by this rule.');}
if(i.mode==='record'&&raw)throw new Error('Turn budget is already recorded. Use correct to replace a valid complete budget.');
if(i.mode==='correct'&&!raw)throw new Error('Turn budget is absent. Use record to create the first complete budget.');
return {narration:s.name+'\'s turn budget is '+(i.mode==='record'?'recorded.':'corrected.'),data:{mode:i.mode,action:next.action,bonusAction:next.bonusAction,reaction:next.reaction,freeInteraction:next.freeInteraction,movementRemainingFeet:next.movementRemainingFeet,previous:previous,sourceRef:next.sourceRef},effects:[{type:i.mode==='record'?'component.add':'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}],events:[],notifications:[]};
