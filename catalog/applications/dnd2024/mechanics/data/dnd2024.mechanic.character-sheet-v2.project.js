var subject=ctx.roles&&ctx.roles.subject;
function object(value){return value!==null&&!Array.isArray(value)&&typeof value==='object';}
function integer(value,min,max){return Number.isSafeInteger(value)&&value>=min&&value<=max;}
function child(key,expectedId){
  var values=ctx.children&&ctx.children[key];
  if(!Array.isArray(values)||values.length!==1||!values[0].roleEntityIds)throw new Error('Exactly one '+key+' projection is required.');
  var role=key==='legacy'?'subject':'root';
  if(values[0].mechanicId!==expectedId||values[0].roleEntityIds[role]!==subject.id)throw new Error('The '+key+' projection is bound to the wrong character.');
  try{var value=JSON.parse(values[0].output&&values[0].output.data||'null');if(!object(value))throw 0;return value;}
  catch(error){throw new Error('The '+key+' projection is unreadable.');}
}
var LABELS={
  str:'Strength',dex:'Dexterity',con:'Constitution',int:'Intelligence',wis:'Wisdom',cha:'Charisma',
  acrobatics:'Acrobatics','animal-handling':'Animal Handling',arcana:'Arcana',athletics:'Athletics',deception:'Deception',history:'History',insight:'Insight',intimidation:'Intimidation',investigation:'Investigation',medicine:'Medicine',nature:'Nature',perception:'Perception',performance:'Performance',persuasion:'Persuasion',religion:'Religion','sleight-of-hand':'Sleight of Hand',stealth:'Stealth',survival:'Survival',
  'dnd2024.vocabulary.ability.strength':'Strength','dnd2024.vocabulary.ability.dexterity':'Dexterity','dnd2024.vocabulary.ability.constitution':'Constitution','dnd2024.vocabulary.ability.intelligence':'Intelligence','dnd2024.vocabulary.ability.wisdom':'Wisdom','dnd2024.vocabulary.ability.charisma':'Charisma',
  'dnd2024.vocabulary.proficiency-rank.proficiency':'Proficiency','dnd2024.vocabulary.proficiency-rank.expertise':'Expertise',
  'dnd2024.equipment.currency.copper-piece':'Copper Piece','dnd2024.equipment.currency.silver-piece':'Silver Piece','dnd2024.equipment.currency.electrum-piece':'Electrum Piece','dnd2024.equipment.currency.gold-piece':'Gold Piece','dnd2024.equipment.currency.platinum-piece':'Platinum Piece'
};
function title(value){return value.split('-').map(function(word){return word?word.charAt(0).toUpperCase()+word.slice(1):word;}).join(' ');}
function label(id){
  if(typeof id!=='string'||!id.length||id.length>200)throw new Error('A named reference id is invalid.');
  if(LABELS[id])return LABELS[id];
  var parts=id.split('.').filter(function(part){return part.length;});
  if(parts.length&&/^v[0-9]+$/i.test(parts[parts.length-1]))parts.pop();
  var value=parts.length?parts[parts.length-1]:id;
  var result=title(value.replace(/_/g,'-'));
  if(!result||/^V[0-9]+$/i.test(result)||result.indexOf('.')>=0)throw new Error('A named reference label is unavailable.');
  return result;
}
function named(id){return {id:id,label:label(id)};}
function namedNullable(id){return id===null?null:named(id);}
function mapNamed(values){return (values||[]).map(named);}
function copyIdentity(value){var out={};['pronouns','appearance','biography','playerNotes'].forEach(function(key){if(typeof value[key]==='string')out[key]=value[key];});return out;}
function mapMeasured(value){return {kind:named(value.id),numerator:value.numerator,denominator:value.denominator,unit:named(value.unitId)};}
function mapSense(value){var out={sense:named(value.id)};if(value.numerator!==undefined){out.numerator=value.numerator;out.denominator=value.denominator;out.unit=named(value.unitId);}return out;}
function collect(nodes,parentItemId,depth,positions,seen){
  if(!Array.isArray(nodes))throw new Error('Character contents are malformed.');
  if(depth>4&&nodes.length)throw new Error('Character inventory exceeds bounded depth.');
  for(var index=0;index<nodes.length;index++){
    var node=nodes[index];
    if(!object(node)||typeof node.id!=='string'||typeof node.name!=='string'||seen[node.id])throw new Error('Character inventory has a duplicate or malformed node.');
    seen[node.id]=true;
    var children=node.contains===undefined?[]:node.contains;
    if(!Array.isArray(children))throw new Error('Character inventory children are malformed.');
    positions[node.id]={parentItemId:parentItemId,order:index,depth:depth,childCount:children.length,deeperContentsOmitted:depth===4};
    if(depth===4&&children.length)throw new Error('Character inventory exceeds bounded depth.');
    collect(children,node.id,depth+1,positions,seen);
  }
}
if(!subject||!object(ctx.input)||Object.keys(ctx.input).length)throw new Error('Character-sheet v2 projection requires one subject and empty input.');
var legacy=child('legacy','dnd2024.mechanic.character-sheet.project');
var currency=child('currency','dnd2024.mechanic.currency-value.read');
if(legacy.version!==1||!legacy.subject||legacy.subject.id!==subject.id||!legacy.inventory||!Array.isArray(legacy.inventory.items))throw new Error('The v1 character projection is invalid.');
if(currency.test!=='currency-value-read'||currency.rootId!==subject.id||!integer(currency.coinCount,0,Number.MAX_SAFE_INTEGER)||!integer(currency.copperValue,0,Number.MAX_SAFE_INTEGER)||!Array.isArray(currency.denominations))throw new Error('The currency projection is invalid.');
var positions={},seen={};collect(subject.contains||[],null,1,positions,seen);
var itemIds={};legacy.inventory.items.forEach(function(item){if(!object(item)||itemIds[item.id])throw new Error('The v1 inventory contains duplicate items.');itemIds[item.id]=true;});
var inventory=legacy.inventory.items.map(function(item){
  var position=positions[item.id];if(!position||position.depth!==item.depth)throw new Error('The v1 inventory does not match the containment projection.');
  var parent=position.parentItemId;if(parent!==null&&!itemIds[parent])throw new Error('An inventory parent is not a projected item.');
  return {id:item.id,name:item.name,definition:named(item.definitionId),quantity:item.quantity,slot:item.slot,parentItemId:parent,order:position.order,depth:position.depth,childCount:position.childCount,deeperContentsOmitted:position.deeperContentsOmitted,equipmentSlots:mapNamed(item.equipmentSlots)};
});
var denominations=currency.denominations.map(function(row){
  if(!object(row)||!integer(row.count,1,Number.MAX_SAFE_INTEGER)||!integer(row.copperValuePerCoin,1,1000)||!integer(row.totalCopperValue,1,Number.MAX_SAFE_INTEGER))throw new Error('A currency denomination is invalid.');
  return {denomination:named(row.denominationId),code:row.code,count:row.count,copperValuePerCoin:row.copperValuePerCoin,totalCopperValue:row.totalCopperValue};
});
var gpCount=0;for(var di=0;di<denominations.length;di++)if(denominations[di].code==='gp')gpCount=denominations[di].count;
var result={version:2,subject:{id:legacy.subject.id,label:legacy.subject.name},inventory:{items:inventory,contentsDepth:4,mayOmitDeeperContents:true},wallet:{coinCount:currency.coinCount,copperValue:currency.copperValue,gpCount:gpCount,denominations:denominations}};
if(legacy.identity)result.identity=copyIdentity(legacy.identity);
if(legacy.origin)result.origin={species:named(legacy.origin.speciesId),background:named(legacy.origin.backgroundId)};
if(legacy.experience)result.experience=legacy.experience;
if(legacy.classes)result.classes=legacy.classes.map(function(value){return {id:value.id,name:value.name,class:named(value.classId),level:value.level,subclass:namedNullable(value.subclassId)};});
['level','proficiencyBonus','hitPoints','temporaryHitPoints','armorClass'].forEach(function(key){if(legacy[key]!==undefined)result[key]=legacy[key];});
if(legacy.abilities)result.abilities=legacy.abilities.map(function(value){return {ability:named(value.id),score:value.score,modifier:value.modifier};});
if(legacy.savingThrows)result.savingThrows=legacy.savingThrows.map(function(value){return {ability:named(value.ability),proficient:value.proficient,modifier:value.modifier};});
if(legacy.skills)result.skills=legacy.skills.map(function(value){return {skill:named(value.id),ability:named(value.ability),proficient:value.proficient,expertise:value.expertise,modifier:value.modifier};});
if(legacy.initiative)result.initiative={ability:named(legacy.initiative.ability),modifier:legacy.initiative.modifier};
if(legacy.body)result.body={size:named(legacy.body.sizeId)};
if(legacy.movement)result.movement=legacy.movement.map(mapMeasured);
if(legacy.senses)result.senses=legacy.senses.map(mapSense);
if(legacy.conditions)result.conditions=legacy.conditions.map(function(value){return {condition:named(value.id),level:value.level};});
if(legacy.proficiencies)result.proficiencies=legacy.proficiencies.map(function(value){return {proficiency:named(value.id),rank:named(value.rankId)};});
if(legacy.features)result.features=legacy.features.map(function(value){return {feature:named(value.featureId),grantedBy:named(value.grantedById),grantKind:named(value.grantKind),classLevel:value.classLevel};});
if(legacy.resources)result.resources=legacy.resources.map(function(value){return {id:value.id,name:value.name,definition:named(value.definitionId),expended:value.expended};});
if(legacy.spellcasting)result.spellcasting=legacy.spellcasting.map(function(value){return {id:value.id,name:value.name,sourceDefinition:named(value.sourceDefinitionId),ability:named(value.abilityId),preparedSpells:mapNamed(value.preparedSpellIds),availableSpells:mapNamed(value.availableSpellIds)};});
if(legacy.actions)result.actions=legacy.actions.map(function(value){return {id:value.id,name:value.name,activities:mapNamed(value.activityIds)};});
return {narration:'Projected the D&D 2024 character sheet v2 for '+subject.name+'.',effects:[],events:[],notifications:[],data:result};
