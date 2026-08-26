var SID='source.dnd2024.srd-5.2.1',LOC='Rules Glossary > Speed',DEF='dnd2024.speed',s=ctx.roles.subject,i=ctx.input;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function speed(v,min){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=1000&&v%5===0;}
function source(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['walkFeet','burrowFeet','climbFeet','flyFeet','swimFeet','sourceRef'])&&speed(v.walkFeet,5)&&speed(v.burrowFeet,0)&&speed(v.climbFeet,0)&&speed(v.flyFeet,0)&&speed(v.swimFeet,0)&&source(v.sourceRef);}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,[]))throw new Error('Reading creature Speed diagnostics requires exactly an empty object input.');
var raw=s.components&&s.components[DEF],state;
if(!raw)return {narration:s.name+' has no Speed.',data:{test:'speed-read',subjectId:s.id,present:false,valid:false,problem:'absent',speed:null},effects:[],events:[],notifications:[]};
try{state=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){return {narration:s.name+' has malformed Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:false,problem:'malformed',speed:null},effects:[],events:[],notifications:[]};}
if(!valid(state))return {narration:s.name+' has invalid Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:false,problem:'invalid',speed:null},effects:[],events:[],notifications:[]};
return {narration:s.name+' has valid Speed.',data:{test:'speed-read',subjectId:s.id,present:true,valid:true,problem:null,speed:state},effects:[],events:[],notifications:[]};
