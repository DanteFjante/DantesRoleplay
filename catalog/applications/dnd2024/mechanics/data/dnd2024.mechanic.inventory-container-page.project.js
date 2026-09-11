var subject=ctx.roles&&ctx.roles.subject,LINK='dnd2024.core.definition-link',QUANTITY='dnd2024.item.quantity',EQUIPMENT='dnd2024.item.equipment',MAX=Number.MAX_SAFE_INTEGER;
function object(value){return value!==null&&!Array.isArray(value)&&typeof value==='object';}
function integer(value,min,max){return Number.isSafeInteger(value)&&value>=min&&value<=max;}
function text(value,max){return typeof value==='string'&&value.length>0&&value.length<=max;}
function optionalParse(raw){try{var value=JSON.parse(raw);return object(value)?value:null;}catch(error){return null;}}
function own(value,key){return value!==null&&typeof value==='object'&&Object.prototype.hasOwnProperty.call(value,key);}
function reference(value){return object(value)&&text(value.entityId,200);}
function title(id){var parts=id.split('.').filter(function(part){return part.length;});if(parts.length&&/^v[0-9]+$/i.test(parts[parts.length-1]))parts.pop();var value=(parts.length?parts[parts.length-1]:id).replace(/_/g,'-');return value.split('-').map(function(word){return word?word.charAt(0).toUpperCase()+word.slice(1):word;}).join(' ');}
function named(id,label){if(!text(id,200))throw new Error('A named inventory reference is invalid.');var value=label||title(id);if(!text(value,400))throw new Error('An inventory label is invalid.');return{id:id,label:value};}
function linked(node){var raw=node.components&&node.components[LINK];if(!raw)return null;var value=optionalParse(raw);return value&&reference(value.definition)?value.definition.entityId:null;}
function quantity(node){var components=node.components;if(!own(components,QUANTITY))return{known:false,value:null};var value=optionalParse(components[QUANTITY]);return{known:value!==null&&integer(value.current,1,MAX),value:value!==null&&integer(value.current,1,MAX)?value.current:null};}
function container(node,definition){
    var malformed=false;
    var sources=[node,definition];
    for(var i=0;i<sources.length;i++){
        var components=sources[i]&&sources[i].components;
        if(!components)continue;
        if(own(components,'dnd2024.item.container')){
            if(optionalParse(components['dnd2024.item.container'])!==null)return{known:true,value:true};
            malformed=true;
        }
        if(own(components,'dnd2024.item-definition')){
            var legacy=optionalParse(components['dnd2024.item-definition']);
            if(legacy&&object(legacy.capacity))return{known:true,value:true};
            if(legacy===null)malformed=true;
        }
    }
    return malformed?{known:false,value:null}:{known:true,value:false};
}
function equipment(node){var components=node.components;if(!own(components,EQUIPMENT))return{known:true,value:[]};var value=optionalParse(components[EQUIPMENT]);if(!value||!reference(value.equippedBy)||!Array.isArray(value.slots)||value.slots.length<1||value.slots.length>32)return{known:false,value:[]};var result=[],seen=Object.create(null);for(var i=0;i<value.slots.length;i++){if(!reference(value.slots[i])||seen[value.slots[i].entityId])return{known:false,value:[]};seen[value.slots[i].entityId]=true;result.push(named(value.slots[i].entityId));}return{known:true,value:result};}
if(!subject||!object(ctx.input)||Object.keys(ctx.input).length||!Array.isArray(subject.contains))throw new Error('Inventory container projection requires one subject and empty input.');
var items=[],seen=Object.create(null),reasons=[];function reason(value){if(reasons.indexOf(value)<0)reasons.push(value);}
for(var index=0;index<subject.contains.length;index++){var node=subject.contains[index];if(items.length>=200)throw new Error('Inventory container exceeds the declared page size.');if(!object(node)||!text(node.id,200)||!text(node.name,400)||seen[node.id]||node.id===subject.id)throw new Error('Inventory containment is malformed or cyclic.');seen[node.id]=true;var definitionId=linked(node),definition=null,count={known:false,value:null},slots={known:false,value:[]},containerState={known:false,value:null};if(definitionId){var value=ctx.references&&ctx.references[definitionId];if(!value||value.id!==definitionId)throw new Error('Item definition is unavailable.');definition=named(definitionId,text(value.name,400)?value.name:null);count=quantity(node);slots=equipment(node);containerState=container(node,value);}else reason('unclassified-content');if(!count.known||!slots.known||!containerState.known)reason('source-incomplete');var itemValue={id:node.id,name:node.name,definition:definition,quantity:count.value,slot:typeof node.slot==='string'?node.slot:'',order:index,equipmentSlots:slots.known?slots.value:undefined,classification:definition?'item':'unclassified',isContainer:containerState.known?containerState.value:undefined};if(!slots.known)delete itemValue.equipmentSlots;if(!containerState.known)delete itemValue.isContainer;items.push(itemValue);}
return{narration:'Projected '+items.length+' direct inventory contents for '+subject.name+'.',effects:[],events:[],notifications:[],data:{version:2,container:named(subject.id,subject.name),state:reasons.length?'partial':'ready',reasons:reasons,items:items,limits:{contentsDepth:1,itemCount:200,directComplete:true,recursiveComplete:false}}};
