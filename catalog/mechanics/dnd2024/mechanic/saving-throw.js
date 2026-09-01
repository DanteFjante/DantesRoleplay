var A={str:"Strength",dex:"Dexterity",con:"Constitution",int:"Intelligence",wis:"Wisdom",cha:"Charisma"},O=["str","dex","con","int","wis","cha"],SID="dnd2024.source.srd-5.2.1",LL="Character Creation > Character Advancement",SL="Playing the Game > Proficiency > Saving Throw Proficiencies",s=ctx.roles.subject,i=ctx.input;
function closed(o,n){if(!o||typeof o!=="object"||Array.isArray(o)){return false;}var k=Object.keys(o).sort(),wanted=n.slice().sort();if(k.length!==wanted.length){return false;}for(var j=0;j<wanted.length;j++){if(k[j]!==wanted[j]){return false;}}return true;}
function sourceRef(r,l){return closed(r,["locator","sourceId"])&&r.sourceId===SID&&r.locator===l;}
if(!i||typeof i!=="object"||Array.isArray(i)){throw new Error("Input must be an object.");}
var k=Object.keys(i).sort(),allow={ability:true,dc:true,rollCircumstances:true,voluntaryFailure:true};
if(k.length<2||k.length>4||k.indexOf("ability")===-1||k.indexOf("dc")===-1){throw new Error("Input must contain ability, dc, and optional rollCircumstances and voluntaryFailure.");}
for(var q=0;q<k.length;q++){if(!allow[k[q]]){throw new Error("Input must contain only ability, dc, optional rollCircumstances, and optional voluntaryFailure.");}}
if(typeof i.ability!=="string"||typeof A[i.ability]==="undefined"){throw new Error("ability must be one exact lowercase D&D ability id.");}
if(typeof i.dc!=="number"||!isFinite(i.dc)||Math.floor(i.dc)!==i.dc||i.dc<0){throw new Error("dc must be a finite nonnegative integer.");}
var hr=k.indexOf("rollCircumstances")!==-1,hv=k.indexOf("voluntaryFailure")!==-1;
if(hv&&typeof i.voluntaryFailure!=="boolean"){throw new Error("voluntaryFailure must be a boolean when present.");}
var circumstances=[],hasAdv=false,hasDis=false,duplicates={};
if(hr){if(!Array.isArray(i.rollCircumstances)){throw new Error("rollCircumstances must be an array when present.");}for(var z=0;z<i.rollCircumstances.length;z++){var c=i.rollCircumstances[z];if(!c||typeof c!=="object"||Array.isArray(c)){throw new Error("Each roll circumstance must be an object.");}var ck=Object.keys(c).sort();if(ck.length!==2||ck[0]!=="kind"||ck[1]!=="source"){throw new Error("Each roll circumstance must contain only kind and source.");}if(c.kind!=="advantage"&&c.kind!=="disadvantage"){throw new Error("roll circumstance kind must be advantage or disadvantage.");}if(typeof c.source!=="string"||c.source.length===0||c.source.trim()!==c.source){throw new Error("roll circumstance source must be a nonempty trimmed string.");}var key=c.kind+"\u0000"+c.source;if(duplicates[key]){throw new Error("rollCircumstances must not repeat an exact kind and source pair.");}duplicates[key]=true;circumstances.push({kind:c.kind,source:c.source});if(c.kind==="advantage"){hasAdv=true;}else{hasDis=true;}}}
if(!s||!s.components){throw new Error("Subject has no component state.");}
if(!s.components["dnd2024.abilities"]){throw new Error("Subject is missing dnd2024.abilities.");}
if(!s.components["dnd2024.character-level"]){throw new Error("Subject is missing dnd2024.character-level.");}
if(!s.components["dnd2024.saving-throw-proficiencies"]){throw new Error("Subject is missing dnd2024.saving-throw-proficiencies.");}
var abilities=JSON.parse(s.components["dnd2024.abilities"]);
if(!closed(abilities,O)){throw new Error("Subject ability state is invalid.");}
for(var a=0;a<O.length;a++){var sc=abilities[O[a]];if(typeof sc!=="number"||!isFinite(sc)||Math.floor(sc)!==sc||sc<1||sc>30){throw new Error("Subject ability state is invalid.");}}
var levelState=JSON.parse(s.components["dnd2024.character-level"]);
if(!closed(levelState,["level","sourceRef"])||typeof levelState.level!=="number"||!isFinite(levelState.level)||Math.floor(levelState.level)!==levelState.level||levelState.level<1||levelState.level>20||!sourceRef(levelState.sourceRef,LL)){throw new Error("Subject character level state is invalid.");}
var saves=JSON.parse(s.components["dnd2024.saving-throw-proficiencies"]);
if(!closed(saves,["abilities","sourceRef"])||!Array.isArray(saves.abilities)||!sourceRef(saves.sourceRef,SL)){throw new Error("Subject saving-throw proficiency state is invalid.");}
var previous=-1;for(var x=0;x<saves.abilities.length;x++){var idx=O.indexOf(saves.abilities[x]);if(typeof saves.abilities[x]!=="string"||idx<0||idx<=previous){throw new Error("Subject saving-throw proficiency list is invalid.");}previous=idx;}
var score=abilities[i.ability],mod=Math.floor((score-10)/2),level=levelState.level,pb=2+Math.floor((level-1)/4),proficient=saves.abilities.indexOf(i.ability)!==-1,mods=[{source:i.ability+" "+score,value:mod}];
if(proficient){mods.push({source:"proficiency (level "+level+"; "+i.ability+" save)",value:pb});}
if(hv&&i.voluntaryFailure){if(circumstances.length!==0){throw new Error("voluntaryFailure requires absent or empty rollCircumstances.");}ctx.log("D&D 2024 "+A[i.ability]+" saving throw: voluntary failure at DC "+i.dc+".");return {narration:s.name+" voluntarily fails a "+A[i.ability]+" saving throw against DC "+i.dc+".",data:{test:"saving-throw",resolution:"voluntary-failure",ability:i.ability,proficient:proficient,dc:i.dc,die:"1d20",rollMode:null,rolls:[],roll:null,rollCircumstances:[],modifiers:mods,total:null,succeeded:false,source:"SRD 5.2.1 - Playing the Game: Saving Throws, Proficiency, and D20 Tests"},effects:[]};}
var rollMode="normal";if(hasAdv&&!hasDis){rollMode="advantage";}else if(hasDis&&!hasAdv){rollMode="disadvantage";}
var rolls=[ctx.randomInt(1,20)];if(rollMode!=="normal"){rolls.push(ctx.randomInt(1,20));}var roll=rolls[0];if(rollMode==="advantage"&&rolls[1]>roll){roll=rolls[1];}if(rollMode==="disadvantage"&&rolls[1]<roll){roll=rolls[1];}
var total=roll;for(var y=0;y<mods.length;y++){total+=mods[y].value;}var succeeded=total>=i.dc;
ctx.log("D&D 2024 "+A[i.ability]+" saving throw ("+rollMode+"): roll "+roll+", total "+total+", DC "+i.dc+".");
return {narration:s.name+" makes a "+A[i.ability]+" saving throw ("+rollMode+"): "+roll+" + modifiers = "+total+" vs DC "+i.dc+" ("+(succeeded?"success":"failure")+").",data:{test:"saving-throw",resolution:"rolled",ability:i.ability,proficient:proficient,dc:i.dc,die:"1d20",rollMode:rollMode,rolls:rolls,roll:roll,rollCircumstances:circumstances,modifiers:mods,total:total,succeeded:succeeded,source:"SRD 5.2.1 - Playing the Game: Saving Throws, Proficiency, and D20 Tests"},effects:[]};
