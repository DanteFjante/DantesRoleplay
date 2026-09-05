var observer = ctx.authorizedObserver, subject = ctx.roles.subject, campaign = ctx.roles.campaign;
function fail() { throw new Error('Item recipes source is unavailable.'); }
function obj(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function text(v, max) { if (typeof v !== 'string' || !v.trim() || v.length > max) fail(); return v; }
function read(components, key) { if (!components || !Object.prototype.hasOwnProperty.call(components, key)) return null; var v = JSON.parse(components[key]); if (!obj(v)) fail(); return v; }
function ref(v) { return obj(v) && typeof v.entityId === 'string' && v.entityId.length > 0 && v.entityId.length <= 200; }
function integer(v) { if (!Number.isSafeInteger(v) || v < 0 || v > 10000) fail(); return v; }
if (!observer || observer.version !== 1 || !subject || !campaign || observer.observerId !== subject.id ||
    observer.campaignId !== campaign.id || !ctx.audience || observer.perspective !== ctx.audience.perspective ||
    !observer.knowledgeComplete || !obj(ctx.input)) fail();
if (Object.keys(ctx.input).sort().join(',') !== 'expectedSourceRevision,itemId,makesOffset,usesOffset') fail();
var itemId = text(ctx.input.itemId, 200), dm = observer.perspective === 'dm';
if (['player','dm'].indexOf(observer.perspective) < 0) fail();
var offsets = { makes: integer(ctx.input.makesOffset), uses: integer(ctx.input.usesOffset) };
if ((offsets.makes || offsets.uses) && !ctx.input.expectedSourceRevision) fail();
if (ctx.input.expectedSourceRevision !== null && ctx.input.expectedSourceRevision !== observer.authorizedSourceRevision) fail();
var item = null, count = 0;
function find(nodes, depth) { nodes.forEach(function(n) { if (++count > 512 || depth > 4) fail(); if (n.id === itemId) { if(item) fail(); item=n; } find(n.contains || [],depth+1); }); }
find(subject.contains || [],1); if (!item) fail();
var states=Object.create(null), facts=Object.create(null), references=ctx.references || {};
observer.knowledge.forEach(function(k) { if (Object.prototype.hasOwnProperty.call(states,k.knowledgeId)) fail(); states[k.knowledgeId]=k.state; });
function state(id) { return states[id] || 'unknown'; }
function visible(s) { return ['known','suspected','believed','doubted','disbelieved'].indexOf(s)>=0; }
Object.keys(references).sort().forEach(function(id) { var f=read(references[id].components,'authorized-knowledge'); if(!f) return;
    if(!dm && (!visible(f.state) || f.state !== state(id))) fail();
    if(!facts[f.subjectId]) facts[f.subjectId]=[]; facts[f.subjectId].push(f);
});
var link=read(item.components,'dnd2024.core.definition-link'), definitionId=link && ref(link.definition) ? link.definition.entityId : null;
var discovery=read(item.components,'dnd2024.magic-item.knowledge');
if(!dm && (state(itemId)!=='known' || state(definitionId)!=='known' || discovery && discovery.identityKnown!==true)) definitionId=null;
var reasons={makes:[],uses:[]}, matches={makes:[],uses:[]};
function reason(group,value) { if(reasons[group].indexOf(value)<0) reasons[group].push(value); }
function both(value) { reason('makes',value);reason('uses',value); }
if(!definitionId) both('dependency-unavailable');
if(!observer.inventoryComplete) both('inventory-bound');
function label(id) { var r=references[id]; return r && (dm || state(id)==='known') && typeof r.name==='string' && r.name.trim() ? r.name.slice(0,160) : null; }
Object.keys(references).sort().forEach(function(id) {
    var recipe=read(references[id].components,'dnd2024.crafting.recipe'); if(!recipe) return;
    var known=facts[id] || []; if(!dm && !known.length) return;
    var stance=known.some(function(f){return f.state==='known';}) ? 'known' : known.length ? known[0].state : 'unknown';
    var primary=known.filter(function(f){return f.state===stance;})[0];
    var incomplete=false, missing=false;
    function links(values) { if(values===undefined) return []; if(!Array.isArray(values)) fail();
        return values.map(function(v){if(!obj(v)||!ref(v.definition)||!Number.isSafeInteger(v.quantity)||v.quantity<1) fail();return v;}); }
    var outputs=links(recipe.outputs), materials=links(recipe.materialRequirements);
    if(!outputs.length) { incomplete=true; both('source-incomplete'); }
    var groups=[];
    if(definitionId && outputs.some(function(v){return v.definition.entityId===definitionId;})) groups.push('makes');
    if(definitionId && materials.some(function(v){return v.definition.entityId===definitionId;})) groups.push('uses');
    if(!groups.length) return;
    var sources=known.slice(0,4).map(function(f){return {label:'Recorded recipe knowledge',knowledgeState:visible(f.state)?f.state:'known'};});
    if(!sources.length) sources=[{label:'Recipe record',knowledgeState:'known'}];
    function displayLinks(values) { if(values.length>16) missing=true;return values.slice(0,16).map(function(v){var name=label(v.definition.entityId);if(!name) missing=true;
        return {name:name || 'Item details unavailable',definitionId:name?v.definition.entityId:null,quantity:v.quantity};}); }
    var out=displayLinks(outputs), mat=displayLinks(materials), tools=[], requirements=[];
    function requirement(value,kind,predicate) {
        if(!obj(value)||value.operator!=='predicate'||value.predicateId!==predicate||!Array.isArray(value.arguments)||value.arguments.length!==1||!ref(value.arguments[0])) {
            incomplete=true; requirements.push({label:kind,value:'Requirement details unavailable',unit:null,sources:sources,observerKnowledge:dm?stance:null});return;
        }
        var name=label(value.arguments[0].entityId); if(!name) missing=true;
        requirements.push({label:kind,value:name || 'Requirement details unavailable',unit:null,sources:sources,observerKnowledge:dm?stance:null});
        if(kind==='Tool proficiency' && name) tools.push(name);
    }
    requirement(recipe.toolRequirement,'Tool proficiency','predicate.proficiency.tool');
    requirement(recipe.crafterRequirement,'Crafter proficiency','predicate.proficiency.crafter');
    var duration=null, d=recipe.workDuration;
    if(obj(d) && d.kind==='measured' && Number.isSafeInteger(d.amount) && d.amount>=0 && ref(d.unit)) {
        var unit=label(d.unit.entityId); if(unit) duration=String(d.amount)+' '+unit; else missing=true;
    } else if(obj(d) && ['instantaneous','permanent','special'].indexOf(d.kind)>=0) duration={instantaneous:'Instantaneous',permanent:'Permanent',special:'Special duration'}[d.kind];
    else missing=true;
    if(recipe.materialCost || Array.isArray(recipe.completionEffects) && recipe.completionEffects.length) missing=true;
    var entry={id:id,name:text(references[id].name || 'Recipe',160),description:primary?text(primary.displayText,1500).slice(0,1024):null,
        knowledgeState:visible(stance)?stance:'known',sources:sources,requirements:requirements,availability:incomplete?'definition-incomplete':'not-evaluated',
        outputs:out,materials:mat,tools:tools,duration:duration,observerKnowledge:dm?stance:null};
    groups.forEach(function(group){matches[group].push(entry);if(incomplete) reason(group,'source-incomplete');if(missing) reason(group,'dependency-unavailable');});
});
var result={version:1,observerId:subject.id,itemId:itemId,perspective:observer.perspective};
['makes','uses'].forEach(function(group){var start=offsets[group],rows=matches[group];if(start>rows.length) fail();
    var entries=rows.slice(start,start+16), more=start+entries.length<rows.length;
    if(more) reason(group,'page-limit');
    result[group]={state:reasons[group].length?'partial':entries.length?'ready':'empty',entries:entries,nextOffset:more?start+entries.length:null,reasons:reasons[group]};
});
// Three bytes per UTF-16 code unit conservatively bounds UTF-8, including surrogate pairs.
function bytes(v){return JSON.stringify(v).length*3;}
var size=bytes(result);
// Measure each removed entry once. Re-encoding a whole shrinking page repeatedly
// wastes the sandbox's memory allowance on long multilingual descriptions.
while(size>62000){var group=result.makes.entries.length>=result.uses.entries.length?'makes':'uses';if(!result[group].entries.length) fail();
    size-=bytes(result[group].entries.pop())+1;result[group].nextOffset=offsets[group]+result[group].entries.length;reason(group,'byte-limit');result[group].state='partial';}
if(bytes(result)>65536 || result.makes.nextOffset===offsets.makes && !result.makes.entries.length || result.uses.nextOffset===offsets.uses && !result.uses.entries.length) fail();
return {narration:'Authorized item recipes.',effects:[],events:[],notifications:[],data:result};
