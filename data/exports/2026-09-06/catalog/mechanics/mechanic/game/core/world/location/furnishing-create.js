var i=ctx.input,FURNISHING='game.core.world.location.furnishing';
function exact(v,keys){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return JSON.stringify(a)===JSON.stringify(b);}
function text(v,n){return typeof v==='string'&&v.trim()===v&&v.length>0&&Array.from(v).length<=n;}
if(!exact(i,['furnishingId','name','status','summary','visibility'])||!/^furnishing\.[a-z0-9]+(?:[.-][a-z0-9]+)*$/.test(i.furnishingId)||!text(i.name,160)||!text(i.summary,1000)||['draft','active','archived'].indexOf(i.status)<0||['public','party','gm'].indexOf(i.visibility)<0)throw new Error('Furnishing input must match the exact closed authored contract.');
var state={status:i.status,summary:i.summary,visibility:i.visibility};
return {narration:i.name+' is created as an unplaced furnishing.',effects:[{type:'entity.create',entityId:i.furnishingId,name:i.name},{type:'component.add',entityId:i.furnishingId,definitionId:FURNISHING,data:JSON.stringify(state)}],events:[],notifications:[],data:{furnishingId:i.furnishingId,state:state,placed:false}};
