var content=ctx.roles.content;
var kinds=['species','background','class','feature','choice-set'];
function only(value,keys){if(value===null||Array.isArray(value)||typeof value!=='object')return false;var actual=Object.keys(value).sort();if(actual.length!==keys.length)return false;for(var i=0;i<keys.length;i++)if(actual[i]!==keys[i])return false;return true;}
if(!only(ctx.input,['contentKey','contentVersion','kind','locator','status']))throw new Error('Input must contain exactly contentKey, contentVersion, kind, locator, and status.');
if(content.components['dnd2024.character.content-definition'])throw new Error('This entity already has an immutable character content definition.');
if(kinds.indexOf(ctx.input.kind)<0)throw new Error('kind must be species, background, class, feature, or choice-set.');
if(typeof ctx.input.contentKey!=='string'||!/^[a-z][a-z0-9-]{0,79}$/.test(ctx.input.contentKey))throw new Error('contentKey must be a canonical lower-case key.');
if(!Number.isSafeInteger(ctx.input.contentVersion)||ctx.input.contentVersion<1||ctx.input.contentVersion>2147483647)throw new Error('contentVersion must be a positive supported integer.');
if(['active','archived'].indexOf(ctx.input.status)<0)throw new Error('status must be active or archived.');
if(typeof ctx.input.locator!=='string'||ctx.input.locator.trim()!==ctx.input.locator||ctx.input.locator.length<4||ctx.input.locator.length>200||!/^.{1,198}PDF page(?:s)? [0-9]+(?:[–-][0-9]+)?$/.test(ctx.input.locator))throw new Error('locator must be a trimmed SRD heading ending with a PDF page reference.');
var state={kind:ctx.input.kind,contentKey:ctx.input.contentKey,contentVersion:ctx.input.contentVersion,status:ctx.input.status,sourceRef:{sourceId:'dnd2024.source.srd-5.2.1',locator:ctx.input.locator}};
return {narration:content.name+' is recorded as immutable '+state.kind+' content '+state.contentKey+' v'+state.contentVersion+'.',effects:[{type:'component.add',entityId:content.id,definitionId:'dnd2024.character.content-definition',data:JSON.stringify(state)}],events:[],notifications:[],data:{contentId:content.id,state:state}};
