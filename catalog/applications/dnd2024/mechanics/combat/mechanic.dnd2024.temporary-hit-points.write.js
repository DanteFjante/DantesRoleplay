var s=ctx.roles.subject,i=ctx.input,DEF='dnd2024.temporary-hit-points',SID='source.dnd2024.srd-5.2.1',LOC='Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)',MAX=9007199254740991;
function closed(v,k){if(!v||typeof v!=='object'||Array.isArray(v)||Object.keys(v).length!==k.length)return false;for(var n=0;n<k.length;n++)if(!Object.prototype.hasOwnProperty.call(v,k[n]))return false;return true;}
function positive(v){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=1&&v<=MAX;}
function valid(v){return closed(v,['amount','sourceRef'])&&positive(v.amount)&&closed(v.sourceRef,['sourceId','locator'])&&v.sourceRef.sourceId===SID&&v.sourceRef.locator===LOC;}
function parse(raw){try{return typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error('Stored Temporary Hit Points are malformed.');}}
if(!s||!s.components)throw new Error('A subject role is required.');
if(!i||typeof i!=='object'||Array.isArray(i)||typeof i.mode!=='string')throw new Error('Input must be a closed Temporary Hit Point transition.');
var raw=s.components[DEF],previous=null;if(raw){previous=parse(raw);if(!valid(previous))throw new Error('Stored Temporary Hit Points are invalid.');}
if(i.mode==='expire'){
 if(!closed(i,['mode']))throw new Error('Expiry requires exactly mode.');
 if(!previous)throw new Error('Temporary Hit Points are absent and cannot expire.');
 return {narration:s.name+' loses '+previous.amount+' Temporary Hit Points.',data:{mode:'expire',previousAmount:previous.amount,grantedAmount:null,resultingAmount:null,kept:false,replaced:false,discardedAmount:previous.amount,sourceRef:previous.sourceRef},effects:[{type:'component.remove',entityId:s.id,definitionId:DEF}],events:[],notifications:[]};
}
if(i.mode!=='grant')throw new Error('mode must be exactly grant or expire.');
var expected=previous?['mode','amount','onExisting']:['mode','amount'];
if(!closed(i,expected)||!positive(i.amount))throw new Error(previous?'An existing buffer grant requires exactly mode, positive amount, and onExisting.':'A first grant requires exactly mode and a positive amount.');
if(previous&&i.onExisting!=='keep'&&i.onExisting!=='replace')throw new Error('onExisting must be exactly keep or replace.');
var source={sourceId:SID,locator:LOC},next={amount:i.amount,sourceRef:source};
if(!previous)return {narration:s.name+' gains '+i.amount+' Temporary Hit Points.',data:{mode:'grant',previousAmount:null,grantedAmount:i.amount,resultingAmount:i.amount,kept:false,replaced:false,discardedAmount:null,sourceRef:source},effects:[{type:'component.add',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}],events:[],notifications:[]};
if(i.onExisting==='keep')return {narration:s.name+' keeps '+previous.amount+' Temporary Hit Points.',data:{mode:'grant',previousAmount:previous.amount,grantedAmount:i.amount,resultingAmount:previous.amount,kept:true,replaced:false,discardedAmount:i.amount,sourceRef:previous.sourceRef},effects:[],events:[],notifications:[]};
return {narration:s.name+' replaces '+previous.amount+' Temporary Hit Points with '+i.amount+'.',data:{mode:'grant',previousAmount:previous.amount,grantedAmount:i.amount,resultingAmount:i.amount,kept:false,replaced:true,discardedAmount:previous.amount,sourceRef:source},effects:[{type:'component.set',entityId:s.id,definitionId:DEF,data:JSON.stringify(next)}],events:[],notifications:[]};
