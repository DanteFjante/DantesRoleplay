var SIZE='dnd2024.creature-size',POS='dnd2024.encounter-position',p=ctx.roles.participant,i=ctx.input;
function closed(v,k){if(!v||typeof v!=='object'||Array.isArray(v))return false;var a=Object.keys(v);if(a.length!==k.length)return false;for(var n=0;n<k.length;n++)if(!Object.prototype.hasOwnProperty.call(v,k[n]))return false;return true;}
function parse(v){try{return typeof v==='string'?JSON.parse(v):v;}catch(x){return null;}}
function integer(v){return typeof v==='number'&&isFinite(v)&&Math.floor(v)===v&&v>=0&&v<=199;}
function size(v){return closed(v,['size'])&&['tiny','small','medium','large','huge','gargantuan'].indexOf(v.size)>=0;}
function pos(v){return closed(v,['encounterId','anchorX','anchorY','sourceRef'])&&typeof v.encounterId==='string'&&v.encounterId.length>0&&integer(v.anchorX)&&integer(v.anchorY)&&closed(v.sourceRef,['sourceId','locator'])&&v.sourceRef.sourceId==='source.dnd2024.srd-5.2.1'&&v.sourceRef.locator==='Playing the Game > Playing on a Grid > Creature Size';}
if(!p||!closed(i,[]))throw new Error('A participant role and exactly empty input are required.');
var sr=p.components&&p.components[SIZE],pr=p.components&&p.components[POS],sv=sr?parse(sr):null,pv=pr?parse(pr):null;
return {narration:p.name+' tactical state is read.',data:{test:'encounter-participant-tactical-state-read',participantId:p.id,sizePresent:!!sr,sizeValid:!!sr&&size(sv),size:!!sr&&size(sv)?sv:null,positionPresent:!!pr,positionValid:!!pr&&pos(pv),position:!!pr&&pos(pv)?pv:null},effects:[]};
