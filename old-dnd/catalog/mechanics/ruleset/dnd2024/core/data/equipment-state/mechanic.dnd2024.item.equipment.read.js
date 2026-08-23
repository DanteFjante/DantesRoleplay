var item=ctx.roles.item;
if(ctx.input===null||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==0)throw new Error('This action takes no input.');
var instance,state;try{instance=JSON.parse(item.components['dnd2024.item-instance']);state=JSON.parse(item.components['dnd2024.equipment-state']);}catch(e){throw new Error('Item equipment state is invalid.');}
if(instance===null||Array.isArray(instance)||typeof instance!=='object'||Object.keys(instance).length!==1||typeof instance.definitionId!=='string'||state===null||Array.isArray(state)||typeof state!=='object'||Object.keys(state).length!==1||['held','worn','unequipped'].indexOf(state.state)<0)throw new Error('Item equipment state is invalid.');
return {narration:item.name+' is '+state.state+'.',effects:[],data:{itemId:item.id,definitionId:instance.definitionId,state:state.state,containerId:item.containerId||null,slot:item.containerSlot}};
