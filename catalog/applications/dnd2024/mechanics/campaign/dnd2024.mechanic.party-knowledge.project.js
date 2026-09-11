var g=ctx.graphSnapshots&&ctx.graphSnapshots.partyKnowledge;
var audience=ctx.audience&&ctx.audience.perspective;
var CAMPAIGN='game.core.campaign.root',WORLD='game.core.world.root',CLOCK='game.core.world.clock';
var PART='game.core.campaign.character-participation',INWORLD='game.core.campaign.in-world';
var HASPART='game.core.campaign.has-character-participation',FORACTOR='game.core.campaign.character-participation.for-actor';
var KINWORLD='game.core.world.knowledge.in-world',ABOUT='game.core.world.knowledge.about';
var STATE='game.core.world.knowledge.state',BASE='game.core.world.knowledge.baseline';
var FACT='game.core.world.fact',RUMOUR='game.core.world.rumour',SECRET='game.core.world.secret',CLUE='game.core.world.clue';
var CLASS='game.core.world.knowledge.classification',VALID='game.core.world.knowledge.validity';
var FACTION='game.core.world.faction',LOCATION='game.core.world.location';
var FINWORLD='game.core.world.faction.in-world',FMEMBER='game.core.world.faction.member';
var contentStates=['known','suspected','believed','doubted','disbelieved'];
var allStates=contentStates.concat(['familiar','unknown']);
var coverage='complete';
var PAGE_ENTRY_LIMIT=40;
var PAGE_BYTE_LIMIT=60000;

function object(v){return v!==null&&!Array.isArray(v)&&typeof v==='object';}
function own(v,k){return object(v)&&Object.prototype.hasOwnProperty.call(v,k);}
function text(v,max){return typeof v==='string'&&v.trim()===v&&v.length>0&&v.length<=max;}
function exact(v,keys){if(!object(v))return false;var a=Object.keys(v).sort(),b=keys.slice().sort();return a.length===b.length&&a.every(function(k,i){return k===b[i];});}
function empty(v){return exact(v,[]);}
function includes(values,value){return values.indexOf(value)!==-1;}
function component(node,id){return node&&object(node.components)&&own(node.components,id)?node.components[id]:null;}
function fingerprint(value){return typeof value==='string'&&/^[0-9A-F]{64}$/.test(value);}
function pageInput(value){
  if(!object(value))return null;
  var keys=Object.keys(value);
  for(var keyIndex=0;keyIndex<keys.length;keyIndex++)if(keys[keyIndex]!=='cursor'&&keys[keyIndex]!=='expectedSourceRevision'&&keys[keyIndex]!=='expectedGraphSourceRevision')return null;
  var hasCursor=own(value,'cursor'),hasRevision=own(value,'expectedSourceRevision'),hasGraphRevision=own(value,'expectedGraphSourceRevision');
  if(hasCursor!==hasRevision||hasCursor!==hasGraphRevision)return null;
  if(!hasCursor)return {offset:0};
  if(typeof value.cursor!=='string'||!/^[1-9][0-9]{0,3}$/.test(value.cursor)||Number(value.cursor)>2048||!fingerprint(value.expectedSourceRevision)||!fingerprint(value.expectedGraphSourceRevision))return null;
  return {offset:Number(value.cursor)};
}
function validGraphPage(value,offset){
  if(!object(value)||!Number.isInteger(value.offset)||!Number.isInteger(value.totalCount)||!Number.isInteger(value.pageSize)||value.offset<0||value.totalCount<0||value.totalCount>2048||value.pageSize!==PAGE_ENTRY_LIMIT||value.offset>value.totalCount||offset!==value.offset)return false;
  var count=Math.min(value.pageSize,value.totalCount-value.offset),hasNext=value.offset+count<value.totalCount;
  return hasNext?typeof value.nextCursor==='string'&&value.nextCursor===String(value.offset+count):value.nextCursor===undefined;
}
function utf8Bytes(value){return encodeURIComponent(JSON.stringify(value)).replace(/%[0-9A-F]{2}/g,'x').length;}
function nodes(step){return g&&g.steps&&g.steps[step]&&Array.isArray(g.steps[step].nodes)?g.steps[step].nodes:[];}
function edges(step){return g&&g.steps&&g.steps[step]&&Array.isArray(g.steps[step].edges)?g.steps[step].edges:[];}
function nodeMap(list){var result={};for(var i=0;i<list.length;i++){var node=list[i];if(!node||!text(node.id,200)||own(result,node.id))return null;result[node.id]=node;}return result;}
function pageData(status,entries,locations,reason,nextCursor){
  var fieldCoverage=status==='unavailable'?'partial':coverage;
  var data={status:status,entries:entries,locations:locations,audience:audience==='dm'?'dm':'party',campaignId:g&&g.root&&text(g.root.id,200)?g.root.id:'unavailable',worldId:worldId||'unavailable',sourceRevision:g&&text(g.sourceRevisionFingerprint,128)?g.sourceRevisionFingerprint:'unavailable',coverage:status==='unavailable'||nextCursor?'partial':fieldCoverage,fieldCoverage:fieldCoverage};
  if(reason)data.reason=reason;
  if(nextCursor)data.nextCursor=nextCursor;
  return data;
}
function outcome(status,entries,locations,reason,nextCursor){return {narration:status==='unavailable'?'Party knowledge is unavailable.':'Projected authorized party knowledge.',effects:[],events:[],notifications:[],data:pageData(status,entries,locations,reason,nextCursor)};}
function invalid(code){return outcome('unavailable',[],[],code,null);}
function uniqueEdges(list){var seen={};for(var i=0;i<list.length;i++){var edge=list[i],key;if(!edge||!text(edge.fromEntityId,200)||!text(edge.toEntityId,200)||!text(edge.kind,200)||typeof edge.revision!=='number'||edge.revision<0)return false;key=edge.fromEntityId+'\n'+edge.toEntityId+'\n'+edge.kind;if(own(seen,key))return false;seen[key]=1;}return true;}
function matching(list,from,to,kind){return list.filter(function(edge){return edge.fromEntityId===from&&(to===null||edge.toEntityId===to)&&edge.kind===kind;});}
function grouped(list,key){var result={};for(var i=0;i<list.length;i++){var value=key(list[i]);if(!own(result,value))result[value]=[];result[value].push(list[i]);}return result;}
function values(group,key){return own(group,key)?group[key]:[];}
function validPrimary(id,value){var statuses=id===FACT?['active','archived']:id===RUMOUR?['unconfirmed','confirmed','disproved','archived']:id===SECRET?['active','archived']:['unrevealed','revealed'];var visibility=id===SECRET?['gm']:id===CLUE?['party','gm']:['public','party','gm'];return object(value)&&includes(statuses,value.status)&&text(value.summary,1000)&&text(value.provenance,500)&&includes(visibility,value.visibility);}
function validClassification(value){return object(value)&&includes(['state','event','identity','relationship','location','capability','rule','quantity','intention','negative'],value.subjectKind)&&includes(['open','discreet','confidential','secret'],value.sensitivity);}
function validInterval(value){return value===null||(object(value)&&Number.isInteger(value.validFromMinute)&&value.validFromMinute>=0&&value.validFromMinute<=1000000000&&(!own(value,'validUntilMinute')||(Number.isInteger(value.validUntilMinute)&&value.validUntilMinute>value.validFromMinute&&value.validUntilMinute<=1000000000)));}
function stateValue(data){return object(data)&&Object.keys(data).length===1&&includes(allStates,data.state)?data.state:null;}
function validBaselineData(data){return object(data)&&Object.keys(data).length===1&&data.inheritance==='current-scope';}
function display(node,primary,subject){var value=node.name+'\n'+primary.summary+(subject?'\n'+subject.name:'');if(value.length>1500){coverage='partial';return value.substring(0,1500);}return value;}

var requestedPage=pageInput(ctx.input);
if(!requestedPage||!g||g.complete!==true||!g.root||!g.steps||!Array.isArray(g.containment)||!fingerprint(g.sourceRevisionFingerprint)||(audience!=='player'&&audience!=='dm'))return invalid('PARTY_KNOWLEDGE_CONTEXT_INVALID');
if(requestedPage.offset>0&&ctx.input.expectedGraphSourceRevision!==g.sourceRevisionFingerprint)return invalid('PARTY_KNOWLEDGE_SOURCE_CHANGED');
var sourcePage=g.page;
if(!validGraphPage(sourcePage,requestedPage.offset))return invalid('PARTY_KNOWLEDGE_PAGE_INVALID');
var required=['world','participations','actors','actorAncestors','knowledge','knowledgeWorlds','subjects','explicitStates','baselineScopes','scopeLinks','scopeAncestors'];
for(var requiredIndex=0;requiredIndex<required.length;requiredIndex++){var requiredStep=g.steps[required[requiredIndex]];if(!requiredStep||requiredStep.complete!==true||!Array.isArray(requiredStep.nodes)||!Array.isArray(requiredStep.edges)||!uniqueEdges(requiredStep.edges))return invalid('PARTY_KNOWLEDGE_GRAPH_INCOMPLETE');}
if(!text(g.root.id,200)||!text(g.root.name,400))return invalid('PARTY_KNOWLEDGE_CAMPAIGN_INVALID');
var campaign=component(g.root,CAMPAIGN);
if(!campaign||campaign.status!=='active'||campaign.rulesetScope!=='dnd2024')return invalid('PARTY_KNOWLEDGE_CAMPAIGN_INVALID');

var worldNodes=nodeMap(nodes('world')),worldEdges=edges('world');
if(!worldNodes||worldEdges.length!==1||worldEdges[0].fromEntityId!==g.root.id||worldEdges[0].kind!==INWORLD||!empty(worldEdges[0].data))return invalid('PARTY_KNOWLEDGE_WORLD_INVALID');
var worldId=worldEdges[0].toEntityId,world=worldNodes[worldId];
var worldRoot=component(world,WORLD),clock=component(world,CLOCK);
if(!world||!worldRoot||worldRoot.status!=='active'||!clock||!Number.isInteger(clock.currentMinute)||clock.currentMinute<0||clock.currentMinute>1000000000)return invalid('PARTY_KNOWLEDGE_WORLD_INVALID');

var partNodes=nodeMap(nodes('participations')),partEdges=edges('participations');
var actorNodes=nodeMap(nodes('actors')),actorEdges=edges('actors');
if(!partNodes||!actorNodes||partEdges.length>20||actorEdges.length>20)return invalid('PARTY_KNOWLEDGE_PARTY_INVALID');
var members=[],actorSeen={};
for(var partIndex=0;partIndex<partEdges.length;partIndex++){
  var partEdge=partEdges[partIndex],partNode=partNodes[partEdge.toEntityId],partComponent=component(partNode,PART);
  if(partEdge.fromEntityId!==g.root.id||partEdge.kind!==HASPART||!empty(partEdge.data)||!partNode||!partComponent||(partComponent.status!=='active'&&partComponent.status!=='withdrawn'))return invalid('PARTY_KNOWLEDGE_PARTY_INVALID');
  var actorLinks=matching(actorEdges,partNode.id,null,FORACTOR);
  if(actorLinks.length!==1||!empty(actorLinks[0].data)||!actorNodes[actorLinks[0].toEntityId])return invalid('PARTY_KNOWLEDGE_PARTY_INVALID');
  if(partComponent.status==='active'){
    var actorNode=actorNodes[actorLinks[0].toEntityId];
    if(own(actorSeen,actorNode.id)||!text(actorNode.name,400))return invalid('PARTY_KNOWLEDGE_PARTY_INVALID');
    actorSeen[actorNode.id]=1;
    members.push({actorId:actorNode.id,actorName:actorNode.name,participationId:partNode.id});
  }
}
if(Object.keys(partNodes).length!==partEdges.length||actorEdges.length!==partEdges.length||members.length>20)return invalid('PARTY_KNOWLEDGE_PARTY_INVALID');
members.sort(function(left,right){return left.actorId<right.actorId?-1:left.actorId>right.actorId?1:0;});

var containmentParents={};
for(var containmentIndex=0;containmentIndex<g.containment.length;containmentIndex++){
  var containment=g.containment[containmentIndex];
  if(!containment||!text(containment.containerEntityId,200)||!text(containment.containedEntityId,200)||!text(containment.slot,200)||typeof containment.revision!=='number'||containment.revision<0)return invalid('PARTY_KNOWLEDGE_CONTAINMENT_INVALID');
  var prior=containmentParents[containment.containedEntityId];
  if(prior&&(prior.containerEntityId!==containment.containerEntityId||prior.slot!==containment.slot||prior.revision!==containment.revision))return invalid('PARTY_KNOWLEDGE_CONTAINMENT_INVALID');
  containmentParents[containment.containedEntityId]=containment;
}
function ancestors(start){var result=[],current=start,seen={};for(var depth=0;depth<16;depth++){var parent=containmentParents[current];if(!parent)break;if(own(seen,current))return null;seen[current]=1;current=parent.containerEntityId;result.push(current);}return result;}
var actorAncestors={};
for(var memberIndex=0;memberIndex<members.length;memberIndex++){var memberAncestors=ancestors(members[memberIndex].actorId);if(memberAncestors===null)return invalid('PARTY_KNOWLEDGE_CONTAINMENT_INVALID');actorAncestors[members[memberIndex].actorId]=memberAncestors;}

var knowledgeNodes=nodeMap(nodes('knowledge')),knowledgeEdges=edges('knowledge');
var knowledgeWorldEdges=edges('knowledgeWorlds'),subjectNodes=nodeMap(nodes('subjects')),subjectEdges=edges('subjects');
var explicitEdges=edges('explicitStates'),baselineEdges=edges('baselineScopes');
var scopeNodes=nodeMap(nodes('baselineScopes')),scopeLinks=edges('scopeLinks'),scopeAncestors={};
if(!knowledgeNodes||!subjectNodes||!scopeNodes||Object.keys(knowledgeNodes).length!==knowledgeEdges.length)return invalid('PARTY_KNOWLEDGE_DOCUMENT_INVALID');
var knowledgeByFrom=grouped(knowledgeEdges,function(edge){return edge.fromEntityId;});
var knowledgeWorldByFrom=grouped(knowledgeWorldEdges,function(edge){return edge.fromEntityId;});
var subjectByFrom=grouped(subjectEdges,function(edge){return edge.fromEntityId;});
var explicitByPair=grouped(explicitEdges,function(edge){return edge.fromEntityId+'\n'+edge.toEntityId;});
var baselineByTo=grouped(baselineEdges,function(edge){return edge.toEntityId;});
var scopeWorldByFrom=grouped(scopeLinks.filter(function(edge){return edge.kind===FINWORLD;}),function(edge){return edge.fromEntityId;});
var scopeMemberByPair=grouped(scopeLinks.filter(function(edge){return edge.kind===FMEMBER;}),function(edge){return edge.fromEntityId+'\n'+edge.toEntityId;});
for(var scopeLinkIndex=0;scopeLinkIndex<scopeLinks.length;scopeLinkIndex++){
  var scopeLink=scopeLinks[scopeLinkIndex];
  if((scopeLink.kind!==FINWORLD&&scopeLink.kind!==FMEMBER)||!empty(scopeLink.data)||!scopeNodes[scopeLink.fromEntityId]||(scopeLink.kind===FINWORLD&&scopeLink.toEntityId!==worldId))return invalid('PARTY_KNOWLEDGE_STATE_INVALID');
}
for(var baselineIndex=0;baselineIndex<baselineEdges.length;baselineIndex++){
  var baselineEdge=baselineEdges[baselineIndex],baselineScope=scopeNodes[baselineEdge.fromEntityId];
  if(baselineEdge.kind!==BASE||!baselineScope)return invalid('PARTY_KNOWLEDGE_STATE_INVALID');
  if(baselineEdge.fromEntityId===worldId){var baselineWorld=component(baselineScope,WORLD);if(!baselineWorld||baselineWorld.status!=='active')return invalid('PARTY_KNOWLEDGE_STATE_INVALID');}
  else if(component(baselineScope,FACTION)){var baselineFaction=component(baselineScope,FACTION),factionWorld=values(scopeWorldByFrom,baselineEdge.fromEntityId);if(baselineFaction.status!=='active'||factionWorld.length!==1||factionWorld[0].toEntityId!==worldId)return invalid('PARTY_KNOWLEDGE_STATE_INVALID');}
  else if(component(baselineScope,LOCATION)){var baselineLocation=component(baselineScope,LOCATION),baselineAncestors=ancestors(baselineEdge.fromEntityId);if(baselineLocation.status!=='active'||baselineLocation.kind!=='region'||baselineAncestors===null||!includes(baselineAncestors,worldId))return invalid('PARTY_KNOWLEDGE_STATE_INVALID');scopeAncestors[baselineEdge.fromEntityId]=baselineAncestors;}
  else return invalid('PARTY_KNOWLEDGE_STATE_INVALID');
}
function scopeApplies(scopeId,actorId){
  if(scopeId===worldId)return true;
  var scope=scopeNodes[scopeId],faction=component(scope,FACTION),location=component(scope,LOCATION);
  if(faction){var memberships=values(scopeMemberByPair,scopeId+'\n'+actorId);if(memberships.length>1)return null;return memberships.length===1;}
  if(location)return includes(actorAncestors[actorId]||[],scopeId);
  return null;
}

var sourceDocuments=[],knowledgeIds=Object.keys(knowledgeNodes).sort();
var pageEntryCount=Math.min(sourcePage.pageSize,sourcePage.totalCount-sourcePage.offset);
if(knowledgeIds.length!==pageEntryCount||knowledgeIds.length>PAGE_ENTRY_LIMIT)return invalid('PARTY_KNOWLEDGE_ENTRY_LIMIT');
for(var knowledgeIndex=0;knowledgeIndex<knowledgeIds.length;knowledgeIndex++){
  var knowledgeId=knowledgeIds[knowledgeIndex],knowledgeNode=knowledgeNodes[knowledgeId];
  var selectedWorldLinks=values(knowledgeByFrom,knowledgeId).filter(function(edge){return edge.toEntityId===worldId&&edge.kind===KINWORLD;});
  var allWorldLinks=values(knowledgeWorldByFrom,knowledgeId);
  var aboutLinks=values(subjectByFrom,knowledgeId);
  if(selectedWorldLinks.length!==1||!empty(selectedWorldLinks[0].data)||allWorldLinks.length!==1||allWorldLinks[0].toEntityId!==worldId||!empty(allWorldLinks[0].data))return invalid('PARTY_KNOWLEDGE_DOCUMENT_INVALID');
  var primaryIds=[FACT,RUMOUR,SECRET,CLUE].filter(function(id){return component(knowledgeNode,id)!==null;});
  var primary=primaryIds.length===1?component(knowledgeNode,primaryIds[0]):null;
  var classification=component(knowledgeNode,CLASS),validity=component(knowledgeNode,VALID);
  var subject=null;
  if(aboutLinks.length===1&&empty(aboutLinks[0].data)&&subjectNodes[aboutLinks[0].toEntityId]&&text(subjectNodes[aboutLinks[0].toEntityId].name,400))subject=subjectNodes[aboutLinks[0].toEntityId];
  else coverage='partial';
  if(primaryIds.length!==1||!text(knowledgeNode.name,400)||!validPrimary(primaryIds[0],primary)||!validClassification(classification)||!validInterval(validity)){coverage='partial';sourceDocuments.push(null);continue;}
  var archived=primary.status==='archived'&&(primaryIds[0]===FACT||primaryIds[0]===RUMOUR||primaryIds[0]===SECRET);
  if(archived||(validity&&validity.validFromMinute>clock.currentMinute)||(validity&&own(validity,'validUntilMinute')&&validity.validUntilMinute<=clock.currentMinute)){sourceDocuments.push(null);continue;}
  var admissions=[],recordInvalid=false;
  if(audience==='player'){
    var recordBaselines=values(baselineByTo,knowledgeId).slice().sort(function(left,right){return left.fromEntityId<right.fromEntityId?-1:left.fromEntityId>right.fromEntityId?1:0;});
    if(recordBaselines.some(function(edge){return !validBaselineData(edge.data);}))recordInvalid=true;
    for(var memberOffset=0;memberOffset<members.length&&!recordInvalid;memberOffset++){
      var member=members[memberOffset];
      var explicit=values(explicitByPair,member.actorId+'\n'+knowledgeId);
      if(explicit.length>1){recordInvalid=true;break;}
      if(explicit.length===1){var explicitState=stateValue(explicit[0].data);if(!explicitState){recordInvalid=true;break;}if(explicitState==='unknown')admissions.push({actorId:member.actorId,actorName:member.actorName,stance:explicitState,source:'explicit',admits:false});else admissions.push({actorId:member.actorId,actorName:member.actorName,stance:explicitState,source:'explicit'});continue;}
      var applicable=null;
      for(var recordBaselineIndex=0;recordBaselineIndex<recordBaselines.length;recordBaselineIndex++){var sourceId=recordBaselines[recordBaselineIndex].fromEntityId;if(sourceId===worldId)continue;var applies=scopeApplies(sourceId,member.actorId);if(applies===null){recordInvalid=true;break;}if(applies){applicable=sourceId;break;}}
      if(recordInvalid)break;
      if(applicable===null&&recordBaselines.some(function(edge){return edge.fromEntityId===worldId;}))applicable=worldId;
      if(applicable!==null)admissions.push({actorId:member.actorId,actorName:member.actorName,stance:'known',source:'baseline',scopeId:applicable});
    }
  }
  if(recordInvalid){coverage='partial';sourceDocuments.push(null);continue;}
  var contentAdmissions=admissions.filter(function(value){return value.admits!==false&&includes(contentStates,value.stance);});
  var familiarAdmissions=admissions.filter(function(value){return value.admits!==false&&value.stance==='familiar';});
  if(audience==='player'&&contentAdmissions.length===0&&familiarAdmissions.length===0){sourceDocuments.push(null);continue;}
  var subjectLocation=component(subject,LOCATION);
  sourceDocuments.push({id:knowledgeId,node:knowledgeNode,subject:subject,location:subjectLocation&&subjectLocation.status==='active'?subjectLocation:null,presentation:primaryIds[0]===RUMOUR?'rumour':primaryIds[0]===CLUE?'evidence':'statement',primary:primary,admissions:admissions,content:contentAdmissions,rawOrdinal:sourcePage.offset+knowledgeIndex+1});
}
var page=[],locations=[],locationIndexes={},processedRawCount=0;
for(var documentIndex=0;documentIndex<sourceDocuments.length;documentIndex++){
  var document=sourceDocuments[documentIndex];
  if(!document){processedRawCount++;continue;}
  var entry;
  if(audience==='player'&&document.content.length===0){
    entry={recognitionKey:g.sourceRevisionFingerprint+'.'+document.rawOrdinal,text:'You recognize this as a familiar topic, but do not remember details.',stance:'familiar',presentationKind:'recognition',admissions:document.admissions.filter(function(value){return value.admits!==false;}).map(function(value){return {actorId:value.actorId,actorName:value.actorName,stance:value.stance,source:value.source};})};
  }else{
    var stances=document.content.map(function(value){return value.stance;}).filter(function(value,index,values){return values.indexOf(value)===index;});
    var stance=audience==='dm'?'dm':stances.length===1?stances[0]:'mixed';
    entry={knowledgeId:document.id,documentRevision:g.sourceRevisionFingerprint,text:display(document.node,document.primary,document.subject),stance:stance,presentationKind:document.presentation,mediaOwnerId:document.id,admissions:audience==='dm'?[]:document.admissions.map(function(value){var result={actorId:value.actorId,actorName:value.actorName,stance:value.stance,source:value.source};if(value.scopeId!==undefined)result.scopeId=value.scopeId;if(value.admits===false)result.admits=false;return result;})};
    if(document.subject)entry.subject={id:document.subject.id,name:document.subject.name};
  }
  var candidatePage=page.concat([entry]),candidateLocations=locations.map(function(location){return {name:location.name,entries:location.entries.slice()};}),candidateIndexes={};
  var locationIds=Object.keys(locationIndexes);
  for(var locationIndex=0;locationIndex<locationIds.length;locationIndex++)candidateIndexes[locationIds[locationIndex]]=locationIndexes[locationIds[locationIndex]];
  if(document.subject&&document.location){
    if(!own(candidateIndexes,document.subject.id)){candidateIndexes[document.subject.id]=candidateLocations.length;candidateLocations.push({name:document.subject.name,entries:[]});}
    candidateLocations[candidateIndexes[document.subject.id]].entries.push(entry);
  }
  var candidateOffset=sourcePage.offset+processedRawCount+1;
  var candidateCursor=candidateOffset<sourcePage.totalCount?String(candidateOffset):null;
  var candidateBytes=utf8Bytes(pageData(candidatePage.length?'ready':'empty',candidatePage,candidateLocations,null,candidateCursor));
  if(candidateBytes>PAGE_BYTE_LIMIT){if(page.length===0)return invalid('PARTY_KNOWLEDGE_ENTRY_SIZE');break;}
  page.push(entry);locations=candidateLocations;locationIndexes=candidateIndexes;processedRawCount++;
}
var nextOffset=sourcePage.offset+processedRawCount;
var nextCursor=nextOffset<sourcePage.totalCount?String(nextOffset):null;
if(utf8Bytes(pageData(page.length?'ready':'empty',page,locations,null,nextCursor))>PAGE_BYTE_LIMIT)return invalid('PARTY_KNOWLEDGE_PAGE_SIZE');
return outcome(page.length?'ready':'empty',page,locations,null,nextCursor);
