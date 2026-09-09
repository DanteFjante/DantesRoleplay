var l=ctx.roles.location,p=ctx.roles.parent,LOCATION='game.core.world.location',ROOT='game.core.world.root';
function exact(v,keys){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function parse(v,n){if(typeof v!=='string')throw new Error(n+' is corrupt.');try{return JSON.parse(v);}catch(e){throw new Error(n+' is corrupt.');}}
function text(v,n){return typeof v==='string'&&v.trim()===v&&v.length>0&&Array.from(v).length<=n;}
function location(v){return exact(v,['kind','status','summary','visibility'])&&['region','settlement','site','interior'].indexOf(v.kind)>=0&&['draft','active','archived'].indexOf(v.status)>=0&&text(v.summary,1000)&&['public','party','gm'].indexOf(v.visibility)>=0;}
function root(v){return exact(v,['status','summary','visibility'])&&['draft','active','archived'].indexOf(v.status)>=0&&text(v.summary,1000)&&['public','party','gm'].indexOf(v.visibility)>=0;}
var pending=exact(ctx.input,['locationId','name','kind','status','summary','visibility'])?ctx.input:null;
if(!p||!p.components||(!l&&!pending)||(l&&pending))throw new Error('Location placement requires a parent and exactly one existing or pending location.');
var child=l?parse(l.components&&l.components[LOCATION],'Location state'):{kind:pending.kind,status:pending.status,summary:pending.summary,visibility:pending.visibility};
var locationId=l?l.id:pending.locationId,locationName=l?l.name:pending.name;
if(locationId===p.id||!location(child)||child.status==='archived')throw new Error('The location shell is invalid or archived.');
if(l&&l.containerId)throw new Error('Only an unplaced location may use the placement primitive.');
var parentLocation=p.components[LOCATION]?parse(p.components[LOCATION],'Parent location state'):null,parentRoot=p.components[ROOT]?parse(p.components[ROOT],'Parent world state'):null;
if((parentLocation?1:0)+(parentRoot?1:0)!==1||(parentLocation&&!location(parentLocation))||(parentRoot&&!root(parentRoot))||(parentLocation&&parentLocation.status!=='active')||(parentRoot&&parentRoot.status!=='active'))throw new Error('The parent must be exactly one active world root or location.');
if(child.kind==='region'&&parentLocation&&parentLocation.kind!=='region')throw new Error('A region may be placed only beneath a world root or another region.');
if(child.kind!=='region'&&parentRoot)throw new Error('A non-region location must be placed beneath an existing location.');
var slot=child.kind==='region'?'region':'location';
return {narration:locationName+' is placed beneath '+p.name+'.',effects:[{type:'containment.move',entityId:locationId,toEntityId:p.id,slot:slot}],events:[],notifications:[],data:{locationId:locationId,parentId:p.id,slot:slot}};
