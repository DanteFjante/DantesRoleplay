var SID='dnd2024.source.srd-5.2.1',LOC='Rules Glossary',DEF='dnd2024.conditions',ORDER=['blinded','charmed','deafened','frightened','grappled','incapacitated','invisible','paralyzed','petrified','poisoned','prone','restrained','stunned','unconscious','exhaustion'],s=ctx.roles.subject,src=ctx.roles.source,i=ctx.input;
var rank={};for(var r=0;r<ORDER.length;r++)rank[ORDER[r]]=r;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var z=0;z<keys.length;z++)if(!Object.prototype.hasOwnProperty.call(v,keys[z]))return false;return true;}
function sourceRef(v){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===LOC;}
function id(v){return typeof v==='string'&&v.length>0&&v.length<=200&&v===v.trim();}
function level(v){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=1&&v<=6;}
function instance(v){if(!v||typeof v!=='object'||Array.isArray(v)||typeof v.condition!=='string')return false;if(v.condition==='exhaustion')return closed(v,['condition','level'])&&level(v.level);return (closed(v,['condition'])||closed(v,['condition','sourceEntityId']))&&Object.prototype.hasOwnProperty.call(rank,v.condition)&&v.condition!=='exhaustion'&&(!Object.prototype.hasOwnProperty.call(v,'sourceEntityId')||id(v.sourceEntityId));}
function compare(a,b){var x=rank[a.condition]-rank[b.condition];if(x!==0)return x;var as=Object.prototype.hasOwnProperty.call(a,'sourceEntityId')?a.sourceEntityId:null,bs=Object.prototype.hasOwnProperty.call(b,'sourceEntityId')?b.sourceEntityId:null;if(as===null)return bs===null?0:-1;if(bs===null)return 1;return as<bs?-1:as>bs?1:0;}
function key(v){return v.condition+'\u0000'+(Object.prototype.hasOwnProperty.call(v,'sourceEntityId')?v.sourceEntityId:'');}
function valid(v){if(!closed(v,['entries','sourceRef'])||!Array.isArray(v.entries)||v.entries.length>100||!sourceRef(v.sourceRef))return false;var seen={},petrified=false,poisoned=false;for(var n=0;n<v.entries.length;n++){if(!instance(v.entries[n])||seen[key(v.entries[n])]||(n>0&&compare(v.entries[n-1],v.entries[n])>=0))return false;seen[key(v.entries[n])]=true;petrified=petrified||v.entries[n].condition==='petrified';poisoned=poisoned||v.entries[n].condition==='poisoned';}return !(petrified&&poisoned);}
function requestedConditions(v){if(!Array.isArray(v)||v.length<1||v.length>14)throw new Error('conditions must be a nonempty array of unique non-Exhaustion SRD Condition ids.');var seen={},out=[];for(var n=0;n<v.length;n++){if(v[n]==='exhaustion')throw new Error('Exhaustion requires exhaust or recover mode.');if(typeof v[n]!=='string'||!Object.prototype.hasOwnProperty.call(rank,v[n]))throw new Error('conditions contains an unknown SRD Condition id.');if(seen[v[n]])throw new Error('conditions cannot repeat a Condition id.');seen[v[n]]=true;out.push(v[n]);}return out;}
function parse(raw){try{return typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('The stored Condition state is malformed.');}}
if(!s)throw new Error('A subject role is required.');
if(!i||typeof i!=='object'||Array.isArray(i)||typeof i.mode!=='string')throw new Error('Input must be an object with a mode.');
var raw=s.components&&s.components[DEF],prior=null,requested,sourceId=src?src.id:null;
if(i.mode==='record'){
 if(!closed(i,['mode']))throw new Error('Recording Conditions requires exactly {"mode":"record"}.');
 if(src)throw new Error('Recording Conditions cannot be scoped to a source.');
 if(raw)throw new Error('The subject already has recorded Condition state.');
 var empty={entries:[],sourceRef:{sourceId:SID,locator:LOC}};
 return {narration:s.name+' now has known empty Condition state.',data:{mode:'record',sourceEntityId:null,beforeEntries:null,afterEntries:[],added:[],removed:[],removedPoisoned:[],previousLevel:0,newLevel:0,levelsChanged:0,lethal:false,sourceRef:empty.sourceRef},effects:[{type:'component.add',entityId:s.id,definitionId:DEF,data:JSON.stringify(empty)}],events:[],notifications:[]};
}
if(i.mode!=='apply'&&i.mode!=='clear'&&i.mode!=='exhaust'&&i.mode!=='recover')throw new Error('mode must be record, apply, clear, exhaust, or recover.');
if(!raw)throw new Error('The subject has no recorded Condition state.');
prior=parse(raw);if(!valid(prior))throw new Error('The stored Condition state is invalid.');
var before=prior.entries,after=before.slice(),added=[],removed=[],removedPoisoned=[],previousLevel=0,newLevel=0,lethal=false;
if(i.mode==='exhaust'||i.mode==='recover'){
 if(!closed(i,['mode','levels'])||!level(i.levels))throw new Error('Exhaustion changes require exactly mode and integer levels from 1 through 6.');
 if(src)throw new Error('Exhaustion changes cannot be scoped to a source.');
 var existing=before.filter(function(x){return x.condition==='exhaustion';})[0];previousLevel=existing?existing.level:0;
 if(i.mode==='exhaust'){newLevel=previousLevel+i.levels;if(newLevel>6)throw new Error('Exhaustion cannot rise above level 6.');}
 else {if(previousLevel===0)throw new Error('The subject is not exhausted.');newLevel=previousLevel-i.levels;if(newLevel<0)throw new Error('Recovery would reduce Exhaustion below level 0.');}
 after=before.filter(function(x){if(x.condition==='exhaustion'){removed.push(x);return false;}return true;});
 if(newLevel>0){var exhaustion={condition:'exhaustion',level:newLevel};after.push(exhaustion);added.push(exhaustion);}lethal=i.mode==='exhaust'&&newLevel===6;
}else{
 if(!closed(i,['mode','conditions']))throw new Error('Applying or clearing Conditions requires exactly mode and conditions.');
 requested=requestedConditions(i.conditions);
 if(i.mode==='apply'){
  for(var a=0;a<requested.length;a++)if((requested[a]==='charmed'||requested[a]==='frightened'||requested[a]==='grappled')&&(!src||src.id===s.id))throw new Error(requested[a]+' requires a non-self source role.');
  var hasPetrified=before.some(function(x){return x.condition==='petrified';});
  if(hasPetrified&&requested.indexOf('poisoned')>=0)throw new Error('Poisoned cannot be applied while Petrified is effective.');
  if(requested.indexOf('petrified')>=0&&requested.indexOf('poisoned')>=0)throw new Error('Petrified and Poisoned cannot be applied together.');
  for(var b=0;b<requested.length;b++){var add={condition:requested[b]};if(sourceId)add.sourceEntityId=sourceId;if(after.some(function(x){return key(x)===key(add);}))throw new Error('That Condition instance is already present for this source.');after.push(add);added.push(add);}
  if(requested.indexOf('petrified')>=0)for(var c=after.length-1;c>=0;c--)if(after[c].condition==='poisoned'){removedPoisoned.push(after[c]);removed.push(after[c]);after.splice(c,1);}
 }else{
  for(var d=0;d<requested.length;d++){var matches=before.filter(function(x){return x.condition===requested[d]&&(!sourceId||x.sourceEntityId===sourceId);});if(matches.length===0)throw new Error('No matching '+requested[d]+' Condition instance can be cleared.');}
  after=before.filter(function(x){var match=requested.indexOf(x.condition)>=0&&(!sourceId||x.sourceEntityId===sourceId);if(match)removed.push(x);return !match;});
 }
}
after.sort(compare);if(after.length>100)throw new Error('Condition state cannot contain more than 100 instances.');
var next={entries:after,sourceRef:prior.sourceRef},data={mode:i.mode,sourceEntityId:sourceId,beforeEntries:before,afterEntries:after,added:added,removed:removed,removedPoisoned:removedPoisoned,previousLevel:previousLevel,newLevel:newLevel,levelsChanged:(i.mode==='exhaust'||i.mode==='recover')?i.levels:0,lethal:lethal,sourceRef:next.sourceRef};
return {narration:s.name+' has Condition state '+(i.mode==='apply'?'updated.':i.mode==='clear'?'cleared.':i.mode==='exhaust'?'exhausted.':'recovered.'),data:data,effects:[{type:'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}],events:[],notifications:[]};
