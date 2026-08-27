var s=ctx.roles.subject,i=ctx.input,DEF='dnd2024.hit-points',SID='source.dnd2024.srd-5.2.1',HLOC='Playing the Game > Damage and Healing > Hit Points',LOC='Playing the Game > Damage and Healing > Healing (PDF p. 17)',MAX=9007199254740991;
function closed(v,k){if(!v||typeof v!=='object'||Array.isArray(v)||Object.keys(v).length!==k.length)return false;for(var n=0;n<k.length;n++)if(!Object.prototype.hasOwnProperty.call(v,k[n]))return false;return true;}
function safe(v,min){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=MAX;}
function validHp(v){return closed(v,['current','maximum','sourceRef'])&&safe(v.current,0)&&safe(v.maximum,1)&&v.current<=v.maximum&&closed(v.sourceRef,['sourceId','locator'])&&v.sourceRef.sourceId===SID&&v.sourceRef.locator===HLOC;}
function parse(raw){try{return typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Stored Hit Points are malformed.');}}
if(!s||!s.components||!s.components[DEF])throw new Error('Healing requires a subject with authoritative Hit Points.');
if(!closed(i,['amount'])||!safe(i.amount,1))throw new Error('Input must contain exactly one positive safe-integer amount.');
var before=parse(s.components[DEF]);if(!validHp(before))throw new Error('Stored Hit Points are invalid.');
var missing=before.maximum-before.current,applied=Math.min(i.amount,missing),afterCurrent=before.current+applied,lost=i.amount-applied,after={current:afterCurrent,maximum:before.maximum,sourceRef:before.sourceRef};
var effects=applied===0?[]:[{type:'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(after)}];
return {narration:s.name+' regains '+applied+' Hit Points.',data:{test:'healing-application',subjectId:s.id,requestedAmount:i.amount,appliedAmount:applied,lostToMaximum:lost,beforeCurrent:before.current,afterCurrent:afterCurrent,maximum:before.maximum,sourceRef:{sourceId:SID,locator:LOC}},effects:effects,events:[],notifications:[]};
