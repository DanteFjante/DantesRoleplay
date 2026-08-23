var SID='source.dnd2024.srd-5.2.1',LOC='Character Creation > Level Advancement',DEF='dnd2024.character-experience',s=ctx.roles.subject,i=ctx.input,MAX=9007199254740991;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function safe(v){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=0&&v<=MAX;}
function source(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['total','sourceRef'])&&safe(v.total)&&source(v.sourceRef);}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,['mode','total']))throw new Error('Input must contain exactly mode and total. Do not supply award amount, campaign, policy, threshold, target level, class, authorization, sourceRef, reason, or effects.');
if(i.mode!=='record'&&i.mode!=='correct')throw new Error('mode must be exactly record or correct.');
if(!safe(i.total))throw new Error('total must be a nonnegative JavaScript-safe integer.');
var next={total:i.total,sourceRef:{sourceId:SID,locator:LOC}},raw=s.components&&s.components[DEF],previous=null;
if(raw){try{previous=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Existing character experience is corrupt and cannot be corrected by this rule.');}if(!valid(previous))throw new Error('Existing character experience is invalid and cannot be corrected by this rule.');}
if(i.mode==='record'&&raw)throw new Error('Character experience is already recorded. Use correct to replace a valid complete total.');
if(i.mode==='correct'&&!raw)throw new Error('Character experience is absent. Use record to create the first total.');
return {narration:s.name+'\'s character experience is '+(i.mode==='record'?'recorded.':'corrected.'),data:{mode:i.mode,total:next.total,previous:previous,sourceRef:next.sourceRef},effects:[{type:i.mode==='record'?'component.add':'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}]};
