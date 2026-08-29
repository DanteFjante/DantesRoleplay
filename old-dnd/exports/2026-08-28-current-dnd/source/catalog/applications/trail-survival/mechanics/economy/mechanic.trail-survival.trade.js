var ids={pin:'trail-survival.scenario-pin',run:'trail-survival.run',route:'trail-survival.route-progress',pending:'trail-survival.pending-choice',outcome:'trail-survival.outcome',scenario:'trail-survival.scenario',party:'trail-survival.party',resources:'trail-survival.resources',conveyance:'trail-survival.conveyance'};
function closed(v,k){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort(),b=k.slice().sort();if(a.length!==b.length)return false;for(var i=0;i<b.length;i++)if(a[i]!==b[i])return false;return true;}
function parse(v,n){if(typeof v!=='string')throw new Error(n+' is corrupt.');try{return JSON.parse(v);}catch(e){throw new Error(n+' is corrupt.');}}
function integer(v,min,max){return typeof v==='number'&&Number.isSafeInteger(v)&&v>=min&&v<=max;}
function text(v,max){return typeof v==='string'&&v.length>0&&v.trim()===v&&Array.from(v).length<=max;}
function nodeWith(nodes,id){for(var i=0;i<(nodes||[]).length;i++)if(nodes[i].components&&nodes[i].components[id])return nodes[i];return null;}
function entry(entries,id){for(var i=0;i<entries.length;i++)if(entries[i].resourceId===id)return entries[i];return null;}
var role=ctx.roles.run;
if(!role||!role.components||!role.components[ids.pin]||!role.components[ids.run]||!role.components[ids.route])throw new Error('Trade requires a complete run root.');
if(role.components[ids.pending])throw new Error('Trade is blocked by a pending choice.');
if(role.components[ids.outcome])throw new Error('Trade is blocked on a terminal run.');
if(!closed(ctx.input,['mode','quantity','resourceId'])||['buy','sell'].indexOf(ctx.input.mode)<0||!text(ctx.input.resourceId,100)||!integer(ctx.input.quantity,1,1000000000))throw new Error('Trade input requires exactly buy/sell mode, resourceId, and positive integer quantity.');
var pin=parse(role.components[ids.pin],'Scenario pin'),run=parse(role.components[ids.run],'Run'),route=parse(role.components[ids.route],'Route progress');
if(!closed(run,['partyId','phase','randomSeed','seedCursor','turn'])||run.phase!=='travel'||!integer(run.turn,0,2147483646)||!integer(run.randomSeed,1,4294967295)||!integer(run.seedCursor,0,2147483646))throw new Error('Trade requires an active bounded travel run.');
var expected=((run.randomSeed>>>0)^Math.imul(run.seedCursor+1,2654435761))>>>0;
if(ctx.seed!==expected)throw new Error('Trade action seed is stale or invalid.');
if(route.activeLegId!==null)throw new Error('Trade is available only at a landmark.');
var reference=ctx.references[pin.scenarioId];if(!reference||!reference.components||!reference.components[ids.scenario])throw new Error('Pinned scenario is unavailable.');
var scenario=parse(reference.components[ids.scenario],'Scenario');
if(scenario.scenarioId!==pin.scenarioId||scenario.scenarioVersion!==pin.scenarioVersion||scenario.scenarioContentHash!==pin.scenarioContentHash||scenario.rulesProfileId!==pin.rulesProfileId)throw new Error('Pinned scenario parity failed.');
if(!scenario.market||!Array.isArray(scenario.market.landmarkIds)||scenario.market.landmarkIds.indexOf(route.currentLandmarkId)<0||!Array.isArray(scenario.market.offers))throw new Error('No scenario market is available at this landmark.');
if(ctx.input.resourceId===scenario.currencyResourceId)throw new Error('The scenario currency cannot trade itself.');
var offer=null;for(var o=0;o<scenario.market.offers.length;o++)if(scenario.market.offers[o].resourceId===ctx.input.resourceId)offer=scenario.market.offers[o];
if(!offer)throw new Error('The requested resource has no market offer.');
var partyNode=nodeWith(role.contains,ids.party);if(!partyNode||!partyNode.components[ids.resources])throw new Error('Trade party resources are unavailable.');
var resources=parse(partyNode.components[ids.resources],'Resources');if(!closed(resources,['entries'])||!Array.isArray(resources.entries))throw new Error('Trade resources are corrupt.');
var nextEntries=resources.entries.map(function(v){return {resourceId:v.resourceId,quantity:v.quantity};});
var currency=entry(nextEntries,scenario.currencyResourceId),item=entry(nextEntries,ctx.input.resourceId);if(!currency)throw new Error('Scenario currency state is missing.');if(!item){item={resourceId:ctx.input.resourceId,quantity:0};nextEntries.push(item);}
var price=ctx.input.mode==='buy'?offer.buyPrice:offer.sellPrice,total=price*ctx.input.quantity;if(!Number.isSafeInteger(total)||total>1000000000)throw new Error('Trade total exceeds the supported bound.');
if(ctx.input.mode==='buy'){if(currency.quantity<total)throw new Error('Trade is not affordable.');if(item.quantity>1000000000-ctx.input.quantity)throw new Error('Trade quantity would overflow.');currency.quantity-=total;item.quantity+=ctx.input.quantity;}else{if(item.quantity<ctx.input.quantity)throw new Error('Trade stock is insufficient.');if(currency.quantity>1000000000-total)throw new Error('Trade currency would overflow.');item.quantity-=ctx.input.quantity;currency.quantity+=total;}
var conveyanceNode=nodeWith(partyNode.contains,ids.conveyance);if(!conveyanceNode)throw new Error('Trade conveyance is unavailable.');var conveyance=parse(conveyanceNode.components[ids.conveyance],'Conveyance');
var weight=0;for(var e=0;e<nextEntries.length;e++){var unit=null;for(var q=0;q<scenario.market.offers.length;q++)if(scenario.market.offers[q].resourceId===nextEntries[e].resourceId)unit=scenario.market.offers[q].unitWeight;if(unit===null)throw new Error('Scenario lacks cargo weight for a stored resource.');var part=unit*nextEntries[e].quantity;if(!Number.isSafeInteger(part)||weight>1000000000-part)throw new Error('Cargo weight exceeds the supported bound.');weight+=part;}
if(weight>conveyance.cargoCapacity)throw new Error('Trade would exceed cargo capacity.');
nextEntries.sort(function(a,b){return a.resourceId<b.resourceId?-1:a.resourceId>b.resourceId?1:0;});
var nextRun={phase:run.phase,turn:run.turn+1,partyId:run.partyId,randomSeed:run.randomSeed,seedCursor:run.seedCursor+1};
return {narration:'Trade completed.',effects:[{type:'component.set',entityId:partyNode.id,definitionId:ids.resources,data:JSON.stringify({entries:nextEntries})},{type:'component.set',entityId:role.id,definitionId:ids.run,data:JSON.stringify(nextRun)}],events:[],data:{command:'trade',mode:ctx.input.mode,resourceId:ctx.input.resourceId,quantity:ctx.input.quantity,total:total,turn:nextRun.turn}};
