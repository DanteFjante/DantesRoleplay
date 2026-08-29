var creature=ctx.roles.creature,sizes=['tiny','small','medium','large','huge','gargantuan'];
if(ctx.input===null||Array.isArray(ctx.input)||typeof ctx.input!=='object'||Object.keys(ctx.input).length!==1||sizes.indexOf(ctx.input.size)<0)throw new Error('Input must contain exactly one valid lower-case D&D Size.');
if(creature.components['dnd2024.creature.body'])throw new Error('This creature already has an explicit body Size; this recorder is write-once.');
var state={sizeRef:{entityId:'dnd2024.vocabulary.size.'+ctx.input.size}};return {narration:creature.name+' is recorded as '+ctx.input.size+'.',effects:[{type:'component.add',entityId:creature.id,definitionId:'dnd2024.creature.body',data:JSON.stringify(state)}],events:[],notifications:[],data:{creatureId:creature.id,size:ctx.input.size}};
