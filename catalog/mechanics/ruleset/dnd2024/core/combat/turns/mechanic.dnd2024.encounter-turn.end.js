var SID='source.dnd2024.srd-5.2.1',LOC='Playing the Game > Combat > The Order of Combat',ORDER='dnd2024.encounter-initiative-order',STATE='dnd2024.encounter-turn-state',e=ctx.roles.encounter,i=ctx.input,MAX=9007199254740991;
function closed(v,keys){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==keys.length)return false;for(var z=0;z<keys.length;z++){if(!Object.prototype.hasOwnProperty.call(v,keys[z]))return false;}return true;}
function safeInt(v,min,max){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=min&&v<=max;}
function source(v,locator){return closed(v,['sourceId','locator'])&&v.sourceId===SID&&v.locator===locator;}
function parse(raw,name){try{return typeof raw==='string'?JSON.parse(raw):raw;}catch(error){throw new Error(name+' is corrupt.');}}
if(!e){throw new Error('An encounter role is required.');}
if(!closed(i,[])){throw new Error('Ending encounter turns requires exactly an empty object input.');}
var snapshot=parse(e.components&&e.components[ORDER],'Encounter Initiative order');
if(!snapshot||!closed(snapshot,['order','sourceRef'])||!source(snapshot.sourceRef,'Playing the Game > Combat > The Order of Combat > Initiative')||!Array.isArray(snapshot.order)||snapshot.order.length<1||snapshot.order.length>100){throw new Error('Encounter Initiative order is missing or invalid.');}
var orderIds={},n,row;
for(n=0;n<snapshot.order.length;n++){
  row=snapshot.order[n];
  if(!closed(row,['participantId','initiative'])||typeof row.participantId!=='string'||row.participantId.length===0||!safeInt(row.initiative,-MAX,MAX)){throw new Error('Encounter Initiative order contains an invalid participant entry.');}
  if(orderIds[row.participantId]){throw new Error('Encounter Initiative order repeats participant '+row.participantId+'.');}
  orderIds[row.participantId]=true;
}
var contents=e.contains||[],roster={};
if(!Array.isArray(contents)||contents.length!==snapshot.order.length){throw new Error('Encounter containment does not match the Initiative order.');}
for(n=0;n<contents.length;n++){
  if(!contents[n]||typeof contents[n].id!=='string'||contents[n].id.length===0){throw new Error('Encounter containment contains an invalid participant.');}
  if(roster[contents[n].id]){throw new Error('Encounter containment repeats participant '+contents[n].id+'.');}
  roster[contents[n].id]=true;
  if(!orderIds[contents[n].id]){throw new Error('Encounter containment does not match the Initiative order.');}
}
for(n=0;n<snapshot.order.length;n++){if(!roster[snapshot.order[n].participantId]){throw new Error('Encounter Initiative order does not match encounter containment.');}}
var current=parse(e.components&&e.components[STATE],'Encounter turn state');
if(!current||!closed(current,['status','round','turnIndex','sourceRef'])||!source(current.sourceRef,LOC)||current.status!=='active'||!safeInt(current.round,1,MAX)||!safeInt(current.turnIndex,0,snapshot.order.length-1)){throw new Error('Encounter turn state is missing, ended, or invalid.');}
var finalParticipant=snapshot.order[current.turnIndex].participantId,next={status:'ended',round:current.round,turnIndex:current.turnIndex,sourceRef:{sourceId:SID,locator:LOC}};
return {narration:e.name+' ends after '+finalParticipant+' in round '+current.round+'.',data:{test:'encounter-turn-end',encounterId:e.id,status:'ended',finalParticipantId:finalParticipant,round:current.round,turnIndex:current.turnIndex,participantCount:snapshot.order.length,source:'SRD 5.2.1 - Playing the Game: Combat > The Order of Combat'},effects:[{type:'component.set',entityId:e.id,definitionId:STATE,data:JSON.stringify(next)}]};
