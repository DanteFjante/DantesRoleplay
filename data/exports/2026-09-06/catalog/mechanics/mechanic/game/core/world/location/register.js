var world = ctx.roles.world, input = ctx.input;
function closed(v, keys) { if (v === null || Array.isArray(v) || typeof v !== 'object') return false; var a = Object.keys(v).sort(); if (a.length !== keys.length) return false; for (var i=0;i<keys.length;i++) if (a[i] !== keys[i]) return false; return true; }
function text(v,min,max){ return typeof v==='string' && v.length>=min && Array.from(v).length<=max && v.trim()===v; }
var validKinds=['settlement','wilderness','landmark','ruin','stronghold','waterway'];
var validVis=['public','party','gm'];
if(!world || !world.id) throw new Error('register-location requires a world role.');
if(!closed(input,['slug','name','kind','summary','visibility'])) throw new Error('Input must be exactly {slug,name,kind,summary,visibility}.');
if(!text(input.slug,1,64) || !/^[a-z0-9-]+$/.test(input.slug)) throw new Error('slug must be lowercase letters, digits, and hyphens only.');
if(!text(input.name,1,120)) throw new Error('name must be 1-120 characters.');
if(validKinds.indexOf(input.kind)<0) throw new Error('kind must be one of: '+validKinds.join(', ')+'.');
if(!text(input.summary,1,1000)) throw new Error('summary must be 1-1000 characters.');
if(validVis.indexOf(input.visibility)<0) throw new Error('visibility must be one of: public, party, gm.');
var worldSlug = world.id.indexOf('world.')===0 ? world.id.slice(6) : world.id;
var entityId = 'location.'+worldSlug+'.'+input.slug;
var data = JSON.stringify({kind:input.kind,status:'active',summary:input.summary,visibility:input.visibility});
return { narration: input.name+' is registered as a new location in '+world.name+'.', effects:[{type:'entity.create',entityId:entityId,name:input.name},{type:'component.add',entityId:entityId,definitionId:'game.core.world.location',data:data}], data:{test:'world-location-register',entityId:entityId,worldId:world.id} };
