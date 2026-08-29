var s=ctx.roles&&ctx.roles.subject,i=ctx.input;
function object(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
function exactKeys(v,keys){if(!object(v))return false;var actual=Object.keys(v).sort(),expected=keys.slice().sort();if(actual.length!==expected.length)return false;for(var n=0;n<expected.length;n++)if(actual[n]!==expected[n])return false;return true;}
function ref(v){return object(v)&&typeof v.entityId==='string'&&v.entityId.length>0&&Object.keys(v).every(function(k){return k==='entityId'||k==='expectedArchetype';});}
function distance(v){return exactKeys(v,['dimension','value','unit'])&&v.dimension==='distance'&&object(v.value)&&exactKeys(v.value,['numerator','denominator'])&&Number.isInteger(v.value.numerator)&&v.value.numerator>=0&&Number.isInteger(v.value.denominator)&&v.value.denominator>=1&&ref(v.unit);}
function valid(v){if(!exactKeys(v,['speeds'])||!object(v.speeds))return false;var ids=Object.keys(v.speeds);if(ids.length<1||ids.length>16)return false;for(var n=0;n<ids.length;n++){var entry=v.speeds[ids[n]];if(!exactKeys(entry,['distance','enabled','sourceRefs'])||!distance(entry.distance)||typeof entry.enabled!=='boolean'||!Array.isArray(entry.sourceRefs)||entry.sourceRefs.length<1||entry.sourceRefs.some(function(source){return !ref(source);}))return false;}return true;}
if(!s)throw new Error('A subject role is required.');
if(!exactKeys(i||{},['mode','speeds']))throw new Error('Input must contain exactly mode and keyed metric speeds.');
if(i.mode!=='record'&&i.mode!=='correct')throw new Error('mode must be exactly record or correct.');
if(!valid({speeds:i.speeds}))throw new Error('speeds must match the keyed metric movement component shape.');
var raw=s.components&&s.components['dnd2024.creature.movement'],previous=null;
if(raw){try{previous=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Existing Speed is corrupt and cannot be corrected by this rule.');}if(!valid(previous))throw new Error('Existing Speed is invalid and cannot be corrected by this rule.');}
if(i.mode==='record'&&raw)throw new Error('Speed is already recorded. Use correct to replace a valid complete profile.');
if(i.mode==='correct'&&!raw)throw new Error('Speed is absent. Use record to create the first profile.');
return {narration:s.name+'\'s Speed is '+(i.mode==='record'?'recorded.':'corrected.'),data:{mode:i.mode,speeds:i.speeds,previous:previous},effects:[{type:i.mode==='record'?'component.add':'component.set',entityId:s.id,definitionId:'dnd2024.creature.movement',data:JSON.stringify({speeds:i.speeds})}]};
