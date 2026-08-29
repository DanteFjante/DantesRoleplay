var s=ctx.roles&&ctx.roles.subject,i=ctx.input;
function object(v){return !!v&&typeof v==='object'&&!Array.isArray(v);}
function exactKeys(v,keys){if(!object(v))return false;var actual=Object.keys(v).sort(),expected=keys.slice().sort();if(actual.length!==expected.length)return false;for(var n=0;n<expected.length;n++)if(actual[n]!==expected[n])return false;return true;}
function ref(v){return object(v)&&typeof v.entityId==='string'&&v.entityId.length>0&&Object.keys(v).every(function(k){return k==='entityId'||k==='expectedArchetype';});}
function distance(v){return exactKeys(v,['dimension','value','unit'])&&v.dimension==='distance'&&object(v.value)&&exactKeys(v.value,['numerator','denominator'])&&Number.isInteger(v.value.numerator)&&v.value.numerator>=0&&Number.isInteger(v.value.denominator)&&v.value.denominator>=1&&ref(v.unit);}
function valid(v){if(!exactKeys(v,['speeds'])||!object(v.speeds))return false;var ids=Object.keys(v.speeds);if(ids.length<1||ids.length>16)return false;for(var n=0;n<ids.length;n++){var entry=v.speeds[ids[n]];if(!exactKeys(entry,['distance','enabled','sourceRefs'])||!distance(entry.distance)||typeof entry.enabled!=='boolean'||!Array.isArray(entry.sourceRefs)||entry.sourceRefs.length<1||entry.sourceRefs.some(function(source){return !ref(source);}))return false;}return true;}
if(!s)throw new Error('A subject role is required.');
if(!exactKeys(i||{},[]))throw new Error('Reading creature Speed diagnostics requires exactly an empty object input.');
var raw=s.components&&s.components['dnd2024.creature.movement'],state;
if(!raw)return {narration:s.name+' has no Speed.',data:{test:'speed-read',subjectId:s.id,present:false,valid:false,problem:'absent',speed:null},effects:[]};
try{state=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){return {narration:s.name+' has malformed Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:false,problem:'malformed',speed:null},effects:[]};}
if(!valid(state))return {narration:s.name+' has invalid Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:false,problem:'invalid',speed:null},effects:[]};
return {narration:s.name+' has valid Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:true,problem:null,speed:state},effects:[]};
