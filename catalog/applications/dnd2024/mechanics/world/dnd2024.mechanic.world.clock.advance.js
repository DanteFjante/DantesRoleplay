var world=ctx.roles.world,ROOT='game.core.world.root',CLOCK='game.core.world.clock',MAX_MINUTE=1000000000,MAX_REVISION=2147483647;
function closed(v,k){if(v===null||Array.isArray(v)||typeof v!=='object')return false;var a=Object.keys(v).sort();if(a.length!==k.length)return false;for(var i=0;i<k.length;i++)if(a[i]!==k[i])return false;return true;}
function parse(v,n){if(typeof v!=='string')throw new Error(n+' is corrupt.');try{return JSON.parse(v);}catch(e){throw new Error(n+' is corrupt.');}}
function text(v,n){return typeof v==='string'&&v.length>0&&v===v.trim()&&Array.from(v).length<=n;}
function integer(v,a,b){return typeof v==='number'&&Number.isSafeInteger(v)&&v>=a&&v<=b;}
if(!closed(ctx.input,['minutes'])||!integer(ctx.input.minutes,1,1440))throw new Error('Clock advance input requires exactly integer minutes from 1 to 1440.');
if(!world||!world.components||!world.components[ROOT]||!world.components[CLOCK])throw new Error('Clock advance requires one projected world root and clock.');
var root=parse(world.components[ROOT],'World root'),clock=parse(world.components[CLOCK],'World clock');
if(!closed(root,['status','summary','visibility'])||root.status!=='active'||!text(root.summary,1000)||['public','party','gm'].indexOf(root.visibility)<0)throw new Error('World root is invalid or inactive.');
if(!closed(clock,['calendarId','currentMinute','revision'])||!text(clock.calendarId,100)||!integer(clock.currentMinute,0,MAX_MINUTE)||!integer(clock.revision,0,MAX_REVISION))throw new Error('World clock is corrupt.');
if(clock.currentMinute>MAX_MINUTE-ctx.input.minutes||clock.revision===MAX_REVISION)throw new Error('World clock cannot advance beyond its confirmed bounds.');
var next={calendarId:clock.calendarId,currentMinute:clock.currentMinute+ctx.input.minutes,revision:clock.revision+1};
return {narration:world.name+' advances by '+ctx.input.minutes+' minutes.',effects:[{type:'clock.advance',entityId:world.id,definitionId:CLOCK,data:JSON.stringify(next),calendarId:clock.calendarId,previousMinute:clock.currentMinute,deltaMinutes:ctx.input.minutes,resultingMinute:next.currentMinute,previousClockRevision:clock.revision,resultingClockRevision:next.revision,eventTypeId:'game.core.world.clock.advanced',subjectEntityId:world.id,activityId:'dnd2024.mechanic.world.clock.advance'}],events:[],notifications:[],data:{test:'dnd2024-world-clock-advance',worldId:world.id,minutes:ctx.input.minutes,calendarId:next.calendarId,previousMinute:clock.currentMinute,currentMinute:next.currentMinute,previousRevision:clock.revision,currentRevision:next.revision}};
