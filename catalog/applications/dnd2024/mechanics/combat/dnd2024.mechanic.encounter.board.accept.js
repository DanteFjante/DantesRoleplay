var ROOT='game.core.campaign.root', SCENE='game.core.campaign.current-scene', BOARD='dnd2024.encounter.board', POS='dnd2024.combat.position', MEDIA='game.core.media.visual';
function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function closed(v,keys){return object(v)&&Object.keys(v).length===keys.length&&keys.every(function(k){return Object.prototype.hasOwnProperty.call(v,k);});}
function integer(v,min,max){return Number.isSafeInteger(v)&&v>=min&&v<=max;}
function read(e,key){var v=JSON.parse(e.components[key]);if(!object(v))throw new Error('Malformed acceptance source.');return v;}
function rect(v,w,h){return closed(v,['x','y','width','height'])&&integer(v.x,0,63)&&integer(v.y,0,63)&&integer(v.width,1,64)&&integer(v.height,1,64)&&v.x+v.width<=w&&v.y+v.height<=h;}
function overlap(a,b){return a.x<b.x+b.width&&a.x+a.width>b.x&&a.y<b.y+b.height&&a.y+a.height>b.y;}
function text(v,max){return typeof v==='string'&&v.trim().length>0&&v.length<=max;}
var c=ctx.roles.campaign,e=ctx.roles.encounter,i=ctx.input;
if(!ctx.audience||ctx.audience.perspective!=='dm')throw new Error('Only the authorized GM may accept a combat-map draft.');
if(!c||!e||!closed(i,['expectedBoardRevision','expectedLocationId','board','background']))throw new Error('Accept requires one exact reviewed draft.');
var scene=read(c,SCENE);
if(read(c,ROOT).status!=='active'||!scene.encounter||scene.encounter.entityId!==e.id||!scene.location||scene.location.entityId!==i.expectedLocationId)throw new Error('The reviewed campaign scene changed.');
var prior=e.components[BOARD]?read(e,BOARD):null;
if((prior?prior.revision:null)!==i.expectedBoardRevision||(prior&&!integer(prior.revision,1,2147483646)))throw new Error('The reviewed board revision changed.');
var b=i.board;
if(!closed(b,['revision','status','visibility','columns','rows','feetPerSquare','terrain','obstacles'])||b.revision!==(prior?prior.revision+1:1)||b.status!=='active'||b.visibility!=='public'||!integer(b.columns,4,64)||!integer(b.rows,4,64)||b.feetPerSquare!==5||!Array.isArray(b.terrain)||b.terrain.length!==0||!Array.isArray(b.obstacles)||b.obstacles.length>32)throw new Error('The reviewed layout is invalid.');
var ids={};
for(var obstacle of b.obstacles){
  if(!closed(obstacle,['id','label','area','blocksMovement','visibility'])||!text(obstacle.id,200)||!/^draft\.obstacle\.[0-9]+$/.test(obstacle.id)||ids[obstacle.id]||!text(obstacle.label,200)||obstacle.visibility!=='public'||obstacle.blocksMovement!==true||!rect(obstacle.area,b.columns,b.rows))throw new Error('A reviewed obstacle is invalid.');
  ids[obstacle.id]=true;
}
for(var n=0;n<b.obstacles.length;n++)for(var m=n+1;m<b.obstacles.length;m++)if(overlap(b.obstacles[n].area,b.obstacles[m].area))throw new Error('Reviewed obstacles overlap.');
for(var row of e.related||[]){
  if(row.kind!=='encounter.has-participation'||row.fromEntityId!==e.id||!row.components[POS])continue;
  var p=read(row,POS);
  if(!p.encounter||p.encounter.entityId!==e.id||!p.anchor||!p.footprint)throw new Error('Current placement is invalid.');
  var area={x:p.anchor.x,y:p.anchor.y,width:p.footprint.width,height:p.footprint.height};
  if(!rect(area,b.columns,b.rows)||b.obstacles.some(function(o){return overlap(o.area,area);}))throw new Error('The layout conflicts with a current participant.');
}
var effects=[{type:prior?'component.set':'component.add',entityId:e.id,definitionId:BOARD,data:JSON.stringify(b)}], mediaOrder=null;
if(i.background!==null){
  var image=i.background,provenance=image.provenance;
  if(!closed(image,['role','visibility','sha256','mimeType','width','height','alt','caption','order','provenance'])||image.role!=='map'||JSON.stringify(image.visibility)!=='["player","dm"]'||!/^[a-f0-9]{64}$/.test(image.sha256)||image.mimeType!=='image/png'||image.width!==b.columns*64||image.height!==b.rows*64||!text(image.alt,500)||typeof image.caption!=='string'||image.caption.length>1000||image.order!==0||!closed(provenance,['kind','credit','source','reviewedOn','version'])||provenance.kind!=='original'||!text(provenance.credit,500)||!text(provenance.source,500)||!/^\d{4}-\d{2}-\d{2}$/.test(provenance.reviewedOn)||provenance.version!==1)throw new Error('The reviewed background metadata is invalid or misaligned.');
  var current=e.components[MEDIA]?read(e,MEDIA):null;
  if(current&&(current.status!=='active'||!Array.isArray(current.attachments)||current.attachments.length>=64))throw new Error('Existing encounter media cannot be safely extended.');
  var attachments=current?current.attachments.slice():[];
  var maxOrder=attachments.reduce(function(max,v){if(!integer(v.order,0,9999))throw new Error('Existing media order is invalid.');return Math.max(max,v.order);},-1);
  var nextImage=JSON.parse(JSON.stringify(image)); nextImage.order=maxOrder+1;
  mediaOrder=nextImage.order;
  if(attachments.some(function(v){return v.sha256===image.sha256&&v.role==='map';}))throw new Error('That background is already attached.');
  attachments.push(nextImage);
  effects.push({type:current?'component.set':'component.add',entityId:e.id,definitionId:MEDIA,data:JSON.stringify({status:'active',attachments:attachments})});
}
effects.push({type:e.components['dnd2024.encounter.board-visual']?'component.set':'component.add',entityId:e.id,definitionId:'dnd2024.encounter.board-visual',data:JSON.stringify({boardRevision:b.revision,mediaOrder:mediaOrder})});
return {narration:'Accepted the explicitly reviewed public combat board.',effects:effects,events:[],notifications:[],data:{encounterId:e.id,boardRevision:b.revision,backgroundAttached:i.background!==null}};
