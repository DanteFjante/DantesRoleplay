var SID='source.dnd2024.srd-5.2.1',XPLOC='Character Creation > Level Advancement',LVLLOC='Character Creation > Character Advancement',XP='dnd2024.character-experience',LEVEL='dnd2024.character-level',s=ctx.roles.subject,i=ctx.input,MAX=9007199254740991,T=[null,0,300,900,2700,6500,14000,23000,34000,48000,64000,85000,100000,120000,140000,165000,195000,225000,265000,305000,355000];
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var n=0;n<keys.length;n++)if(!Object.prototype.hasOwnProperty.call(v,keys[n]))return false;return true;}
function safe(v){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=0&&v<=MAX;}
function xpSource(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===XPLOC;}
function levelSource(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LVLLOC;}
function validXp(v){return closed(v,['total','sourceRef'])&&safe(v.total)&&xpSource(v.sourceRef);}
function validLevel(v){return closed(v,['level','sourceRef'])&&typeof v.level==='number'&&isFinite(v.level)&&Math.floor(v.level)===v.level&&v.level>=1&&v.level<=20&&levelSource(v.sourceRef);}
function state(raw,valid){if(!raw)return {present:false,valid:false,problem:'absent',value:null};var value;try{value=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){return {present:true,valid:false,problem:'malformed',value:null};}return valid(value)?{present:true,valid:true,problem:null,value:value}:{present:true,valid:false,problem:'invalid',value:null};}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,[]))throw new Error('Reading character experience eligibility requires exactly an empty object input.');
var x=state(s.components&&s.components[XP],validXp),l=state(s.components&&s.components[LEVEL],validLevel),experience={present:x.present,valid:x.valid,problem:x.problem,total:x.value?x.value.total:null},level={present:l.present,valid:l.valid,problem:l.problem,totalLevel:l.value?l.value.level:null};
if(!x.valid||!l.valid)return {narration:s.name+'\'s character experience eligibility is unknown.',data:{test:'character-experience-read',subjectId:s.id,experience:experience,characterLevel:level,status:'unknown',nextLevel:null,nextThreshold:null},effects:[]};
if(l.value.level===20)return {narration:s.name+' is at the total character level cap.',data:{test:'character-experience-read',subjectId:s.id,experience:experience,characterLevel:level,status:'at-level-cap',nextLevel:null,nextThreshold:null},effects:[]};
var next=l.value.level+1,threshold=T[next],eligible=x.value.total>=threshold;
return {narration:s.name+' is '+(eligible?'eligible for':'below')+' the next total character level threshold.',data:{test:'character-experience-read',subjectId:s.id,experience:experience,characterLevel:level,status:eligible?'eligible-for-next-level':'below-next-threshold',nextLevel:next,nextThreshold:threshold},effects:[]};
