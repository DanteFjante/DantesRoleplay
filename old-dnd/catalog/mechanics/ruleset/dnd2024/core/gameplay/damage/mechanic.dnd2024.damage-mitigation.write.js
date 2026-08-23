var DEF='dnd2024.damage-mitigation',SID='source.dnd2024.srd-5.2.1',LOC='Playing the Game > Damage and Healing',ORDER=['acid','bludgeoning','cold','fire','force','lightning','necrotic','piercing','poison','psychic','radiant','slashing','thunder'],s=ctx.roles.subject,i=ctx.input;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var actual=Object.keys(v).sort(),n;if(actual.length!==keys.length)return false;for(n=0;n<keys.length;n++)if(actual[n]!==keys[n])return false;return true;}
function canonical(v){if(!Array.isArray(v)||v.length>ORDER.length)return null;var found={},result=[],n,type,index,previous=-1;for(n=0;n<v.length;n++){type=v[n];index=ORDER.indexOf(type);if(index<0||found[type])return null;found[type]=true;result.push(type);}result.sort(function(a,b){return ORDER.indexOf(a)-ORDER.indexOf(b);});return result;}
function ordered(v){var normalized=canonical(v),n;if(!normalized||normalized.length!==v.length)return false;for(n=0;n<v.length;n++)if(v[n]!==normalized[n])return false;return true;}
function source(v){return closed(v,['locator','sourceId'])&&v.sourceId===SID&&v.locator===LOC;}
function valid(v){return closed(v,['immunities','resistances','sourceRef','vulnerabilities'])&&ordered(v.resistances)&&ordered(v.immunities)&&ordered(v.vulnerabilities)&&source(v.sourceRef);}
if(!s)throw new Error('A subject role is required.');
if(!closed(i,['immunities','mode','resistances','vulnerabilities']))throw new Error('Input must contain exactly mode, resistances, immunities, and vulnerabilities. Do not supply sourceRef, damage, condition, Hit Points, events, or effects.');
if(i.mode!=='record'&&i.mode!=='correct')throw new Error('input.mode must be exactly "record" or "correct".');
var resistances=canonical(i.resistances),immunities=canonical(i.immunities),vulnerabilities=canonical(i.vulnerabilities);
if(!resistances||!immunities||!vulnerabilities)throw new Error('Each mitigation list must be an array containing only unique canonical D&D 2024 damage types.');
var raw=s.components&&s.components[DEF],present=!!(s.components&&Object.prototype.hasOwnProperty.call(s.components,DEF)),previous=null;
if(present){try{previous=typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('The existing damage-mitigation component is corrupt and cannot be corrected by this rule.');}if(!valid(previous))throw new Error('The existing damage-mitigation component is invalid and cannot be corrected by this rule.');}
if(i.mode==='record'&&present)throw new Error('Damage mitigation is already recorded. Use mode "correct" to replace the complete known mitigation state.');
if(i.mode==='correct'&&!present)throw new Error('Damage mitigation is absent. Use mode "record" to establish the complete known mitigation state.');
var next={resistances:resistances,immunities:immunities,vulnerabilities:vulnerabilities,sourceRef:{sourceId:SID,locator:LOC}};
return {narration:s.name+'\'s damage mitigation is '+(i.mode==='record'?'recorded.':'corrected.'),data:{mode:i.mode,resistances:next.resistances,immunities:next.immunities,vulnerabilities:next.vulnerabilities,previous:previous,sourceRef:next.sourceRef},effects:[{type:i.mode==='record'?'component.add':'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}]};
