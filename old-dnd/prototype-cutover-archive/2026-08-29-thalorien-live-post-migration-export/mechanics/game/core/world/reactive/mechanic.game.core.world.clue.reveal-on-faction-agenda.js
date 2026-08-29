var clue=ctx.roles.clue;
function closed(v,k){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort();if(a.length!==k.length)return false;for(var i=0;i<k.length;i++)if(a[i]!==k[i])return false;return true;}
function value(v){if(typeof v==='string'){try{return JSON.parse(v);}catch(e){return null;}}return v;}
function validClue(c){return closed(c,['provenance','status','summary','visibility'])&&typeof c.summary==='string'&&typeof c.provenance==='string'&&c.summary.trim()===c.summary&&c.provenance.trim()===c.provenance;}
if(!clue||!clue.components||!clue.components['game.core.world.clue'])throw new Error('Fixed clue is unavailable.');
var current=value(clue.components['game.core.world.clue']); if(!validClue(current))throw new Error('Fixed clue state is corrupt.');
var p=ctx.event&&ctx.event.payload; var before=value(p&&p.before), after=value(p&&p.after);
if(!p||p.entityId!=='faction.feature-03.fixture'||p.definitionId!=='game.core.world.faction'||!before||!after||!before.agenda||!after.agenda||before.agenda.state!=='ready'||after.agenda.state!=='advanced')return {effects:[]};
if(current.status==='revealed'&&current.visibility==='party')return {effects:[]};
if(current.status!=='unrevealed'||current.visibility!=='gm')throw new Error('Fixed clue state is not revealable.');
var next={status:'revealed',summary:current.summary,provenance:current.provenance,visibility:'party'};
return {narration:clue.name+' is revealed by the Compact agenda change.',effects:[{type:'component.set',entityId:clue.id,definitionId:'game.core.world.clue',data:JSON.stringify(next)}],data:{test:'agenda-triggered-clue-reveal',clueId:clue.id}};
