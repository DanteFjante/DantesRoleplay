var allowed=["str","dex","con","int","wis","cha"];
function exact(value,keys){if(value===null||Array.isArray(value)||typeof value!=="object")return false;var actual=Object.keys(value).sort(),expected=keys.slice().sort();if(actual.length!==expected.length)return false;for(var i=0;i<expected.length;i++)if(actual[i]!==expected[i])return false;return true;}
var subject=ctx.roles.subject,input=ctx.input;
if(!subject||!subject.components)throw new Error("Saving-throw proficiencies require a subject.");
if(!exact(input,["abilities"])||!Array.isArray(input.abilities)||input.abilities.length>6)throw new Error("Saving-throw-proficiency input must contain exactly abilities as an array.");
var seen={},abilities=[];for(var index=0;index<input.abilities.length;index++){var ability=input.abilities[index];if(typeof ability!=="string"||allowed.indexOf(ability)<0||seen[ability])throw new Error("abilities must contain unique exact D&D ability ids.");seen[ability]=true;}
for(var orderIndex=0;orderIndex<allowed.length;orderIndex++)if(seen[allowed[orderIndex]])abilities.push(allowed[orderIndex]);
var record={abilities:abilities,sourceRef:{sourceId:"source.dnd2024.srd-5.2.1",locator:"Playing the Game > Proficiency > Saving Throw Proficiencies"}},previous=subject.components["dnd2024.saving-throw-proficiencies"]||null;
return {narration:subject.name+"'s saving-throw proficiencies are recorded.",data:{abilities:abilities,previousAbilities:previous===null?null:JSON.parse(previous).abilities},effects:[{type:previous===null?"component.add":"component.set",entityId:subject.id,definitionId:"dnd2024.saving-throw-proficiencies",data:JSON.stringify(record)}],events:[],notifications:[]};
