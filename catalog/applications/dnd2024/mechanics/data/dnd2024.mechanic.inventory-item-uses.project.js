var observer = ctx.authorizedObserver, subject = ctx.roles.subject, campaign = ctx.roles.campaign;
function fail() { throw new Error('Item uses source is unavailable.'); }
function obj(v) { return v !== null && typeof v === 'object' && !Array.isArray(v); }
function text(v, max) { if (typeof v !== 'string' || !v.trim() || v.length > max) fail(); return v; }
function read(c, key) { if (!c || !Object.prototype.hasOwnProperty.call(c,key)) return null; var v=JSON.parse(c[key]); if(!obj(v)) fail(); return v; }
function ref(v) { return obj(v) && typeof v.entityId==='string' && v.entityId.length>0 && v.entityId.length<=200; }
if (!observer || observer.version!==1 || !subject || !campaign || observer.observerId!==subject.id || observer.campaignId!==campaign.id ||
    !ctx.audience || observer.perspective!==ctx.audience.perspective || !observer.knowledgeComplete || !obj(ctx.input)) fail();
if(Object.keys(ctx.input).sort().join(',')!=='expectedSourceRevision,itemId,offset') fail();
var itemId=text(ctx.input.itemId,200), offset=ctx.input.offset, dm=observer.perspective==='dm';
if(['player','dm'].indexOf(observer.perspective)<0 || !Number.isSafeInteger(offset) || offset<0 || offset>10000 || offset && !ctx.input.expectedSourceRevision) fail();
if(ctx.input.expectedSourceRevision!==null && ctx.input.expectedSourceRevision!==observer.authorizedSourceRevision) fail();
var item=null,count=0;
function find(nodes,depth) { nodes.forEach(function(n){if(++count>512 || depth>4) fail();if(n.id===itemId){if(item) fail();item=n;}find(n.contains||[],depth+1);}); }
find(subject.contains||[],1); if(!item) fail();
var references=ctx.references||{}, states=Object.create(null), facts=[], links=Object.create(null), rows=[], reasons=[];
observer.knowledge.forEach(function(k){if(Object.prototype.hasOwnProperty.call(states,k.knowledgeId)) fail();states[k.knowledgeId]=k.state;});
function state(id){return states[id]||'unknown';}
function visible(s){return ['known','suspected','believed','doubted','disbelieved'].indexOf(s)>=0;}
function reason(r){if(reasons.indexOf(r)<0)reasons.push(r);}
function label(r){var target=ref(r)?references[r.entityId]:null;return target && (dm||state(r.entityId)==='known') && typeof target.name==='string' && target.name.trim()?target.name.slice(0,160):null;}
var definitionLink=read(item.components,'dnd2024.core.definition-link'), definitionId=definitionLink&&ref(definitionLink.definition)?definitionLink.definition.entityId:null;
var discovery=read(item.components,'dnd2024.magic-item.knowledge'), definition=references[definitionId];
var identity=dm || state(itemId)==='known' && state(definitionId)==='known' && (!discovery || discovery.identityKnown===true);
if(!identity || !definitionId) reason('dependency-unavailable');
if(!observer.inventoryComplete) reason('inventory-bound');
var owners=[item];if(definition)owners.push(definition);
owners.forEach(function(owner){var membership=read(owner.components,'dnd2024.activity.membership');if(!membership)return;
 if(!Array.isArray(membership.activities))fail();membership.activities.forEach(function(v){if(!ref(v))fail();links[v.entityId]=true;});});
Object.keys(references).sort().forEach(function(id){var f=read(references[id].components,'authorized-knowledge');if(!f)return;
 if(!dm && (!visible(f.state)||f.state!==state(id)))fail();
 if(f.subjectId===itemId || f.subjectId===definitionId || identity && links[f.subjectId])facts.push({id:id,f:f});
});
function property(label,value,sources,knowledge,unit){return {label:label.slice(0,100),value:value,unit:unit||null,sources:sources,observerKnowledge:dm?knowledge:null};}
function entry(id,name,kind,knowledge,sources){return {id:text(id,200),name:text(name,160),description:null,kind:kind,knowledgeState:visible(knowledge)?knowledge:'known',
 sources:sources,requirements:[],costs:[],effects:[],availability:'not-evaluated',executionSupport:'unsupported',observerKnowledge:dm?knowledge:null};}
if(identity)Object.keys(links).sort().forEach(function(id){
 if(!dm && state(id)!=='known')return;
 var target=references[id];if(!target){reason('dependency-unavailable');return;}
 var sources=[{label:'Canonical activity record',knowledgeState:'known'}], row=entry(id,target.name||'Recorded activity','canonical-activity',state(id),sources), c=target.components;
 var presentation=read(c,'dnd2024.core.presentation');if(presentation && presentation.summary)row.description=text(presentation.summary,1500).slice(0,1024);
 function prop(label,value){return property(label,value,sources,state(id));}
 function missing(label){reason('dependency-unavailable');row.requirements.push(prop(label,'Supporting details unavailable'));}
 function amount(v){if(Number.isSafeInteger(v)&&v>=0)return String(v);
   if(obj(v)&&Number.isSafeInteger(v.count)&&v.count>0&&Number.isSafeInteger(v.modifier)){var die=label(v.dieRef);if(die)return v.count+' × '+die+(v.modifier?' '+(v.modifier>0?'+':'')+v.modifier:'');}return null;}
 var activation=read(c,'dnd2024.activity.activation');
 if(activation && typeof activation.economy==='string'){
   var economy={action:'Action','bonus-action':'Bonus action',reaction:'Reaction',none:'No separate action cost',special:'Special activation'}[activation.economy];
   if(!economy)fail();row.costs.push(prop('Activation',economy));if(activation.amount!==undefined)row.costs.push(prop('Activation units',activation.amount));
 }else if(activation)missing('Activation time');
 if(activation && (activation.condition || activation.trigger))missing('Activation prerequisites');
 var payments=read(c,'dnd2024.activity.cost');if(payments){if(!Array.isArray(payments.payments))fail();
   payments.payments.slice(0,10).forEach(function(p){var name=label(p.resource), value=amount(p.amount);if(!name||value===null||p.alternativeGroup){missing('Resource cost');return;}
     row.costs.push(prop(name+' ('+p.timing+')',value));});if(payments.payments.length>10)reason('source-incomplete');}
 var attack=read(c,'dnd2024.activity.attack');if(attack)row.requirements.push(prop('Attack mode',text(attack.mode,80)));
 var damage=read(c,'dnd2024.activity.damage');if(damage){if(!Array.isArray(damage.parts))fail();damage.parts.slice(0,8).forEach(function(part){var value=amount(part.amount),type=label(part.damageType);
   if(value!==null && type)row.effects.push(value+' '+type+' damage ('+damage.delivery+').');else missing('Damage');});if(damage.parts.length>8)reason('source-incomplete');}
 var healing=read(c,'dnd2024.activity.healing');if(healing){var value=amount(healing.amount),type=label(healing.healingType);if(value!==null&&type&&!healing.scaling)row.effects.push(value+' '+type+' healing.');else missing('Healing');}
 var check=read(c,'dnd2024.activity.check');if(check){
   ['abilityOptions','proficiencySources'].forEach(function(key){if(!check[key])return;if(!Array.isArray(check[key]))fail();var names=check[key].map(label);if(names.some(function(n){return !n;}))missing('Check training');else row.requirements.push(prop(key==='abilityOptions'?'Check abilities':'Check proficiency',names.join(', ').slice(0,512)));});
   if(Number.isSafeInteger(check.difficulty))row.requirements.push(prop('Check difficulty',check.difficulty));else missing('Check difficulty');if(check.outcomes && check.outcomes.length)missing('Check outcomes');}
 var applied=read(c,'dnd2024.activity.applied-effects');if(applied){if(!Array.isArray(applied.applications))fail();applied.applications.slice(0,3).forEach(function(a){var name=label(a.effect);if(name)row.effects.push(name+' ('+a.timing+', '+a.recipient+').');else missing('Applied effect');});if(applied.applications.length>3)reason('source-incomplete');}
 ['range','targeting','save','duration','sequence'].forEach(function(key){if(read(c,'dnd2024.activity.'+key))missing(key.charAt(0).toUpperCase()+key.slice(1));});
 if(!presentation && !activation && !attack && !damage && !healing && !check && !applied){row.availability='definition-incomplete';reason('source-incomplete');}
 if(row.requirements.length>12){row.requirements=row.requirements.slice(0,12);reason('source-incomplete');}
 rows.push(row);
});
// Inline fixed activities have no per-activity knowledge identity. Raw fields stay
// DM-only; Player statements are handled separately and never grant these fields.
if(dm)owners.forEach(function(owner,ownerIndex){var inline=read(owner.components,'dnd2024.item-activity');if(!inline)return;
 if(!Array.isArray(inline.activities))fail();var ids=Object.create(null);
 inline.activities.slice().sort(function(a,b){return a.id<b.id?-1:a.id>b.id?1:0;}).forEach(function(a,index){
   if(ids[a.id])fail();ids[a.id]=true;
   var sources=[{label:'Canonical fixed item activity',knowledgeState:'known'}], row=entry('inline-use.'+ownerIndex+'.'+index,'Consume and create item','canonical-activity',state(owner.id),sources);
   row.description='A recorded fixed activity consumes this stack and creates an item in the same container.';
   if(a.kind!=='consume-and-grant-item' || !Number.isSafeInteger(a.consumeQuantity) || a.consumeQuantity<1 || !obj(a.grant)) {row.availability='definition-incomplete';reason('source-incomplete');}
   else {row.costs.push(property('Item quantity',a.consumeQuantity,sources,state(owner.id)));row.effects.push('Create '+text(a.grant.name,160)+'.');
     row.requirements.push(property('Source stack','Fungible, directly contained, with no contents',sources,state(owner.id)));
     row.requirements.push(property('Activation','Timing is not specified by this activity',sources,state(owner.id)));
     if(owner.id===definitionId)row.executionSupport='supported';
     var quantity=read(item.components,'dnd2024.item.quantity'), fixed=read(definition && definition.components,'dnd2024.item-definition');
     if(quantity && quantity.current<a.consumeQuantity || (item.contains||[]).length || fixed && fixed.stackPolicy!=='fungible')row.availability='requirements-not-met';
   }rows.push(row);
 });
});
// A known statement is shown verbatim as a statement. Its words are not parsed
// into effects, costs, eligibility, or an executable capability.
facts.forEach(function(record){var f=record.f, s=visible(f.state)?f.state:'known';
 var row=entry(record.id,'Recorded item statement','recorded-application',f.state,[{label:'Character knowledge record',knowledgeState:s}]);
 row.description=text(f.displayText,1500).slice(0,1024);row.executionSupport='adjudication-required';
 row.requirements.push(property('Interpretation','A DM must adjudicate any proposed application of this statement.',row.sources,f.state));rows.push(row);
});
rows.sort(function(a,b){return a.id<b.id?-1:a.id>b.id?1:0;});
for(var i=1;i<rows.length;i++)if(rows[i-1].id===rows[i].id)fail();
if(offset>rows.length)fail();var entries=rows.slice(offset,offset+32),more=offset+entries.length<rows.length;
if(more)reason('page-limit');
var group={state:reasons.length?'partial':entries.length?'ready':'empty',entries:entries,nextOffset:more?offset+entries.length:null,reasons:reasons};
var result={version:1,observerId:subject.id,itemId:itemId,perspective:observer.perspective,uses:group};
function bytes(v){return JSON.stringify(v).length*3;}
var size=bytes(result);while(size>62000){if(!entries.length)fail();size-=bytes(entries.pop())+1;group.nextOffset=offset+entries.length;reason('byte-limit');group.state='partial';}
if(bytes(result)>65536 || group.nextOffset===offset && !entries.length)fail();
return {narration:'Authorized item uses.',effects:[],events:[],notifications:[],data:result};
