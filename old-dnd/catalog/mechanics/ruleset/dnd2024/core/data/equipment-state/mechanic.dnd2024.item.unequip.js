var item=ctx.roles.item,holder=ctx.roles.holder;
if(ctx.input===null||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==0)throw new Error('This action takes no input.');
if(item.containerId!==holder.id)throw new Error('The item must be directly contained by the named holder before it can be unequipped.');
var raw=item.components['dnd2024.equipment-state'];if(!raw)throw new Error('The item is not equipped.');
var state;try{state=JSON.parse(raw);}catch(e){throw new Error('Equipment state is invalid.');}if(state===null||Array.isArray(state)||typeof state!=='object'||Object.keys(state).length!==1||['held','worn'].indexOf(state.state)<0)throw new Error('Equipment state is invalid.');
return {narration:item.name+' is unequipped by '+holder.name+'.',effects:[{type:'component.set',entityId:item.id,definitionId:'dnd2024.equipment-state',data:JSON.stringify({state:'unequipped'})}],data:{itemId:item.id,holderId:holder.id,previousState:state.state,state:'unequipped'}};
