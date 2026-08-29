import { readGameServerContext } from './prototype/dnd2024/src/server/game-server-context.js';
const envelope = await readGameServerContext({serverOrigin:'http://localhost:6217'});
console.log('status', envelope.status);
if (envelope.status === 'connected') {
  console.log('seat', envelope.audience.seat, 'perspective', envelope.audience.perspective);
  console.log('locDir length', envelope.locationDirectory?.length ?? 0);
  console.log(envelope.locationDirectory?.slice(0,10).map((l)=>`${l.id}:${l.name}`).join('\n'));
}
