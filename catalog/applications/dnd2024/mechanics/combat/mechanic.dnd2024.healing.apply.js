var subject=ctx.roles&&ctx.roles.subject,input=ctx.input,definitionId='dnd2024.creature.hit-points',maxSafe=9007199254740991;
function object(value){return !!value&&typeof value==='object'&&!Array.isArray(value);}
function exact(value,keys){if(!object(value))return false;var actual=Object.keys(value).sort(),expected=keys.slice().sort();if(actual.length!==expected.length)return false;for(var n=0;n<expected.length;n++)if(actual[n]!==expected[n])return false;return true;}
function safe(value,min){return Number.isInteger(value)&&value>=min&&value<=maxSafe;}
function valid(value){return object(value)&&((exact(value,['current','maximum'])||exact(value,['current','maximum','maximumReduction'])))&&safe(value.current,0)&&safe(value.maximum,1)&&value.current<=value.maximum&&(!Object.prototype.hasOwnProperty.call(value,'maximumReduction')||safe(value.maximumReduction,0));}
if(!subject||!subject.components||!subject.components[definitionId])throw new Error('Healing requires a subject with authoritative Hit Points.');
if(!exact(input||{},['amount'])||!safe(input.amount,1))throw new Error('Input must contain exactly a positive safe integer amount.');
var before;try{before=typeof subject.components[definitionId]==='string'?JSON.parse(subject.components[definitionId]):subject.components[definitionId];}catch(error){throw new Error('The stored Hit Point state is malformed.');}
if(!valid(before))throw new Error('The stored Hit Point state is invalid.');
var missing=before.maximum-before.current,appliedAmount=Math.min(input.amount,missing),afterCurrent=before.current+appliedAmount,after={current:afterCurrent,maximum:before.maximum};
if(Object.prototype.hasOwnProperty.call(before,'maximumReduction'))after.maximumReduction=before.maximumReduction;
ctx.log('Applied '+appliedAmount+' healing to '+subject.name+': '+before.current+' -> '+afterCurrent+'.');
return {narration:subject.name+' regains '+appliedAmount+' Hit Points: '+before.current+' to '+afterCurrent+'.',effects:[{type:'component.set',entityId:subject.id,definitionId:definitionId,data:JSON.stringify(after)}],data:{test:'healing-application',subjectId:subject.id,requestedAmount:input.amount,appliedAmount:appliedAmount,lostToMaximum:input.amount-appliedAmount,beforeCurrent:before.current,afterCurrent:afterCurrent,maximum:before.maximum}};
