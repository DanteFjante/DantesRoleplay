var creature=ctx.roles.creature; var sizes=['tiny','small','medium','large','huge','gargantuan'];
if(ctx.input===null||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==1||sizes.indexOf(ctx.input.size)<0)throw new Error('Input must contain exactly a valid lower-case D&D Size.');
if(creature.components['dnd2024.creature-size'])throw new Error('This creature already has an explicit Size; this mechanic records once.');
return {narration:creature.name+' is recorded as '+ctx.input.size+'.',effects:[{type:'component.add',entityId:creature.id,definitionId:'dnd2024.creature-size',data:JSON.stringify({size:ctx.input.size})}],data:{creatureId:creature.id,size:ctx.input.size}};
