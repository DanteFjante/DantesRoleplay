var i=ctx.input,LOCATION='game.core.world.location';
function exact(v,keys){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function text(v,n){return typeof v==='string'&&v.trim()===v&&v.length>0&&Array.from(v).length<=n;}
if(!exact(i,['kind','locationId','name','status','summary','visibility'])||!/^location\.[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(i.locationId)||!text(i.name,160)||!text(i.summary,1000)||['region','settlement','site','interior'].indexOf(i.kind)<0||['draft','active','archived'].indexOf(i.status)<0||['public','party','gm'].indexOf(i.visibility)<0)throw new Error('Location shell input must match the exact closed authored contract.');
var state={kind:i.kind,status:i.status,summary:i.summary,visibility:i.visibility};
return {narration:i.name+' is created as an unplaced location shell.',effects:[{type:'entity.create',entityId:i.locationId,name:i.name},{type:'component.add',entityId:i.locationId,definitionId:LOCATION,data:JSON.stringify(state)}],events:[],notifications:[],data:{locationId:i.locationId,state:state,placed:false}};
