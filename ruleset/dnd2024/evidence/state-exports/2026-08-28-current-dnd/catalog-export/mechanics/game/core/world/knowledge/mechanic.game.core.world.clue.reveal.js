var clue = ctx.roles.clue, world = ctx.roles.world, clueComponent = 'game.core.world.clue', scopeKind = 'game.core.world.knowledge.in-world';
function closed(v, keys) { if (v === null || Array.isArray(v) || typeof v !== 'object') return false; var a = Object.keys(v).sort(); if (a.length !== keys.length) return false; for (var i=0;i<keys.length;i++) if (a[i] !== keys[i]) return false; return true; }
function parse(v) { if (typeof v !== 'string') throw new Error('Clue state is corrupt.'); try { return JSON.parse(v); } catch (e) { throw new Error('Clue state is corrupt.'); } }
function text(v,n) { return typeof v === 'string' && v.length >= 1 && v.trim() === v && Array.from(v).length <= n; }
if (!closed(ctx.input, [])) throw new Error('Clue reveal input must be exactly {}.');
if (!clue || !world || !clue.components || !clue.components[clueComponent]) throw new Error('Clue reveal requires clue and world roles.');
var current = parse(clue.components[clueComponent]);
if (!closed(current,['provenance','status','summary','visibility']) || !text(current.summary,1000) || !text(current.provenance,500) || (current.status !== 'unrevealed' && current.status !== 'revealed') || (current.visibility !== 'gm' && current.visibility !== 'party')) throw new Error('Clue state is corrupt.');
if (current.status !== 'unrevealed' || current.visibility !== 'gm') throw new Error('Only an unrevealed GM-only clue may be revealed.');
if (!Array.isArray(clue.relationships)) throw new Error('Clue scope projection is missing.');
var count=0; for (var i=0;i<clue.relationships.length;i++) { var e=clue.relationships[i]; if (!closed(e,['data','fromEntityId','kind','toEntityId']) || typeof e.data !== 'string') throw new Error('Clue relationship projection is corrupt.'); if (e.kind === scopeKind) { if (e.fromEntityId !== clue.id || e.toEntityId !== world.id || !closed(parse(e.data),[])) throw new Error('Clue has corrupt scope state.'); count++; } }
if (count !== 1) throw new Error('Clue must have exactly one stored scope link to the supplied world.');
var next={status:'revealed',summary:current.summary,provenance:current.provenance,visibility:'party'};
return { narration: clue.name + ' is revealed.', effects:[{type:'component.set',entityId:clue.id,definitionId:clueComponent,data:JSON.stringify(next)}], data:{test:'world-clue-reveal',clueId:clue.id,previousStatus:'unrevealed',currentStatus:'revealed',previousVisibility:'gm',currentVisibility:'party'} };
