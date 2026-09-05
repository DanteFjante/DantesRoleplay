var ROOT = 'game.core.campaign.root', SCENE = 'game.core.campaign.current-scene';
var BOARD = 'dnd2024.encounter.board', POSITION = 'dnd2024.combat.position';
function object(value) { return value !== null && !Array.isArray(value) && typeof value === 'object'; }
function closed(value, keys) { return object(value) && Object.keys(value).length === keys.length && keys.every(function(key) { return Object.prototype.hasOwnProperty.call(value, key); }); }
function integer(value, min, max) { return Number.isSafeInteger(value) && value >= min && value <= max; }
function read(entity, key) { var value = JSON.parse(entity.components[key]); if (!object(value)) throw new Error('Malformed draft source.'); return value; }
if (!ctx.audience || ctx.audience.perspective !== 'dm') throw new Error('Only the GM may request a draft.');
var campaign = ctx.roles.campaign, encounter = ctx.roles.encounter, input = ctx.input;
if (!campaign || !encounter || !closed(input, ['columns','rows','obstacleCount','seed','setting','prompt']) ||
    !integer(input.columns,4,64) || !integer(input.rows,4,64) || !integer(input.obstacleCount,0,32) ||
    !integer(input.seed,0,2147483647) || ['woodland','ruin','chamber'].indexOf(input.setting) < 0 ||
    typeof input.prompt !== 'string' || input.prompt.length > 600) throw new Error('Invalid bounded map request.');
var scene = read(campaign, SCENE), root = read(campaign, ROOT);
if (root.status !== 'active' || !scene.encounter || scene.encounter.entityId !== encounter.id || !scene.location)
  throw new Error('The encounter is no longer the active campaign scene.');
var location = ctx.references && ctx.references[scene.location.entityId];
if (!location || read(location, 'game.core.world.location').status !== 'active') throw new Error('The current location is unavailable.');
var prior = encounter.components[BOARD] ? read(encounter, BOARD) : null;
if (prior && !integer(prior.revision,1,2147483646)) throw new Error('The existing board revision is invalid.');
var occupied = [];
for (var row of encounter.related || []) {
  if (row.kind !== 'encounter.has-participation' || row.fromEntityId !== encounter.id || !row.components[POSITION]) continue;
  var position = read(row, POSITION);
  if (!position.encounter || position.encounter.entityId !== encounter.id || !position.anchor || !position.footprint ||
      !integer(position.anchor.x,0,63) || !integer(position.anchor.y,0,63) || !integer(position.footprint.width,1,8) || !integer(position.footprint.height,1,8) ||
      position.anchor.x + position.footprint.width > input.columns || position.anchor.y + position.footprint.height > input.rows)
    throw new Error('The requested grid does not contain all current positions.');
  occupied.push(position);
}
var randomState = input.seed || 123456789;
function random() { randomState ^= randomState << 13; randomState ^= randomState >>> 17; randomState ^= randomState << 5; return (randomState >>> 0) / 4294967296; }
var candidates = [];
for (var y=1; y<input.rows-1; y++) for (var x=1; x<input.columns-1; x++) {
  if (x === Math.floor(input.columns/2) || y === Math.floor(input.rows/2)) continue;
  if (occupied.some(function(p) { return x >= p.anchor.x && x < p.anchor.x+p.footprint.width && y >= p.anchor.y && y < p.anchor.y+p.footprint.height; })) continue;
  candidates.push({x:x,y:y,width:1,height:1});
}
if (candidates.length < input.obstacleCount) throw new Error('Not enough clear squares for that obstacle count.');
for (var n=candidates.length-1;n>0;n--) { var selected=Math.floor(random()*(n+1)), swap=candidates[n]; candidates[n]=candidates[selected]; candidates[selected]=swap; }
var label = input.setting === 'woodland' ? 'Tree' : input.setting === 'ruin' ? 'Masonry' : 'Pillar';
var obstacles = candidates.slice(0,input.obstacleCount).map(function(area,index) { return { id:'draft.obstacle.'+(index+1),label:label+' '+(index+1),area:area,blocksMovement:true,visibility:'public' }; });
return { narration:'Prepared an inert GM map layout. Explicit review and acceptance are still required.',effects:[],events:[],notifications:[],data:{
  version:1,campaignId:campaign.id,encounterId:encounter.id,locationId:location.id,expectedBoardRevision:prior?prior.revision:null,
  board:{revision:prior?prior.revision+1:1,status:'active',visibility:'public',columns:input.columns,rows:input.rows,feetPerSquare:5,terrain:[],obstacles:obstacles},
  backgroundRequest:{prompt:'Top-down '+input.setting+' battle map. Align to '+input.columns+' by '+input.rows+' squares; no labels or tokens. '+input.prompt,width:input.columns*64,height:input.rows*64,mimeType:'image/png'},
  provider:'catalog-deterministic',model:'square-layout-v1',seed:input.seed
}};
