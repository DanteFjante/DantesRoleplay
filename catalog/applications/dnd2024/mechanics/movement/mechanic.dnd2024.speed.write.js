var SID='source.dnd2024.srd-5.2.1',LOC='Rules Glossary > Speed',DEF='dnd2024.speed',s=ctx.roles.subject,i=ctx.input;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function speed(v,min){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=1000&&v%5===0;}
function source(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['walkFeet','burrowFeet','climbFeet','flyFeet','swimFeet','sourceRef'])&&speed(v.walkFeet,5)&&speed(v.burrowFeet,0)&&speed(v.climbFeet,0)&&speed(v.flyFeet,0)&&speed(v.swimFeet,0)&&source(v.sourceRef);}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,['mode','walkFeet','burrowFeet','climbFeet','flyFeet','swimFeet']))throw new Error('Input must contain exactly mode and the five base speeds. Do not supply provenance, remaining movement, terrain, position, route, pace, or effects.');
if(i.mode!=='record'&&i.mode!=='correct')throw new Error('mode must be exactly record or correct.');
var next={walkFeet:i.walkFeet,burrowFeet:i.burrowFeet,climbFeet:i.climbFeet,flyFeet:i.flyFeet,swimFeet:i.swimFeet,sourceRef:{sourceId:SID,locator:LOC}};
if(!valid(next))throw new Error('walkFeet must be a 5..1000 multiple of five and each special Speed must be a 0..1000 multiple of five.');
var raw=s.components&&s.components[DEF],previous=null;
if(raw){try{previous=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Existing Speed is corrupt and cannot be corrected by this rule.');}if(!valid(previous))throw new Error('Existing Speed is invalid and cannot be corrected by this rule.');}
if(i.mode==='record'&&raw)throw new Error('Speed is already recorded. Use correct to replace a valid complete profile.');
if(i.mode==='correct'&&!raw)throw new Error('Speed is absent. Use record to create the first profile.');
return {narration:s.name+'\'s Speed is '+(i.mode==='record'?'recorded.':'corrected.'),data:{mode:i.mode,walkFeet:next.walkFeet,burrowFeet:next.burrowFeet,climbFeet:next.climbFeet,flyFeet:next.flyFeet,swimFeet:next.swimFeet,previous:previous,sourceRef:next.sourceRef},effects:[{type:i.mode==='record'?'component.add':'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}],events:[],notifications:[]};
