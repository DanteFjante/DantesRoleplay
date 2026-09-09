var SID='dnd2024.source.srd-5.2.1',LOC='Playing the Game > Combat > The Order of Combat > Initiative',DEF='dnd2024.encounter-initiative-order',e=ctx.roles.encounter,i=ctx.input;
if(!e){throw new Error('An encounter role is required.');}
if(!i||typeof i!=='object'||Array.isArray(i)){throw new Error('Input must be an object.');}
var ik=Object.keys(i),n;
for(n=0;n<ik.length;n++){if(ik[n]!=='participants'&&ik[n]!=='tieDecisions'){throw new Error('Input must contain only participants and optional tieDecisions.');}}
if(!i.participants||typeof i.participants!=='object'||Array.isArray(i.participants)){throw new Error('participants must be an object keyed by participant id.');}
if(e.components&&e.components[DEF]){throw new Error('This encounter already has an Initiative order; correcting it is not authorized by this rule.');}
var contents=e.contains||[];
if(contents.length===0){throw new Error('The encounter contains no participants.');}
var roster={},ids=[];
for(n=0;n<contents.length;n++){if(roster[contents[n].id]){throw new Error('Participant '+contents[n].id+' is contained twice.');}roster[contents[n].id]=true;ids.push(contents[n].id);}
var pk=Object.keys(i.participants);
if(pk.length!==ids.length){throw new Error('participants must contain exactly one entry per roster participant; expected '+ids.length+', received '+pk.length+'.');}
for(n=0;n<pk.length;n++){if(!roster[pk[n]]){throw new Error('participants names '+pk[n]+', which this encounter does not contain.');}}
if(!ctx.children||!ctx.children.initiative){throw new Error('This host did not supply composed Initiative results. The sandbox must expose ctx.children; rebuild and restart the server before running this rule.');}
var kids=ctx.children.initiative;
if(kids.length!==ids.length){throw new Error('Expected one Initiative result per participant; received '+kids.length+' for '+ids.length+' participant(s).');}
var rows=[],got={};
for(n=0;n<kids.length;n++){
var k=kids[n],sub=k.roleEntityIds?k.roleEntityIds.subject:'';
if(!sub||!roster[sub]){throw new Error('An Initiative result named a participant this encounter does not contain.');}
if(got[sub]){throw new Error('Participant '+sub+' produced more than one Initiative result.');}
got[sub]=true;
var d;
try{d=JSON.parse(k.output&&k.output.data?k.output.data:'{}');}catch(err){throw new Error('An Initiative result was not readable.');}
if(!d||d.test!=='initiative'||typeof d.initiative!=='number'||Math.floor(d.initiative)!==d.initiative){throw new Error('An Initiative result did not carry an Initiative count.');}
rows.push({participantId:sub,initiative:d.initiative});
}
rows.sort(function(x,y){return y.initiative-x.initiative;});
var groups=[];
for(n=0;n<rows.length;n++){if(n>0&&rows[n].initiative===rows[n-1].initiative){groups[groups.length-1].push(rows[n]);}else{groups.push([rows[n]]);}}
var tied=[];
for(n=0;n<groups.length;n++){if(groups[n].length>1){tied.push(groups[n]);}}
var dec=i.tieDecisions===undefined?[]:i.tieDecisions;
if(!Array.isArray(dec)){throw new Error('tieDecisions must be an array of ordered id groups.');}
if(dec.length!==tied.length){throw new Error('tieDecisions must contain exactly one ordered group per tied Initiative count; expected '+tied.length+', received '+dec.length+'.');}
for(n=0;n<tied.length;n++){
var g=tied[n],choice=dec[n],member={},m,ordered=[],used={};
if(!Array.isArray(choice)||choice.length!==g.length){throw new Error('Each tie decision must order exactly the participants tied at that count, highest first.');}
for(m=0;m<g.length;m++){member[g[m].participantId]=g[m];}
for(m=0;m<choice.length;m++){
var pid=choice[m];
if(typeof pid!=='string'||!member[pid]){throw new Error('A tie decision named a participant that is not tied at that Initiative count.');}
if(used[pid]){throw new Error('A tie decision repeated '+pid+'.');}
used[pid]=true;ordered.push(member[pid]);}
g.length=0;
for(m=0;m<ordered.length;m++){g.push(ordered[m]);}
}
var order=[],lines=[],q;
for(n=0;n<groups.length;n++){for(q=0;q<groups[n].length;q++){order.push({participantId:groups[n][q].participantId,initiative:groups[n][q].initiative});lines.push(groups[n][q].participantId+' '+groups[n][q].initiative);}}
ctx.log('Encounter Initiative order recorded for '+order.length+' participant(s); '+tied.length+' tied count(s) resolved by decision.');
return {narration:'Initiative order for '+e.name+': '+lines.join(', ')+'.',data:{test:'encounter-initiative-order',participants:order.length,tiedCounts:tied.length,order:order,source:'SRD 5.2.1 - Playing the Game: Combat > The Order of Combat > Initiative'},effects:[{type:'component.add',entityId:e.id,definitionId:DEF,data:JSON.stringify({order:order,sourceRef:{sourceId:SID,locator:LOC}})}]};
