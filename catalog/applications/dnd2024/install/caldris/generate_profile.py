"""Build the complete Caldris installation from committed authored sources.

The retained runtime capture supplies authored entities, never SQL/runtime history.
The later atlas and clean-map packet own geography. Unreviewed old markers on a
replacement image are omitted; the places remain available in the directory.
"""
import argparse
import copy
import hashlib
import json
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]
HERE = Path(__file__).resolve().parent
SNAPSHOT = ROOT / 'data/exports/current'
WORLD = 'world.caldris'
CAMPAIGN = 'campaign.caldris.measure-of-mercy'
SPACE = 'dnd2024-main'
ALIASES = {
    'region.caldris.chalklands': 'location.caldris.atlas.chalklands',
    'location.caldris.button-hills': 'location.caldris.alderwick.the-button-hills',
    'location.caldris.highmead': 'location.caldris.alderwick.highmead',
    'location.caldris.house-of-the-ninth-angle': 'location.caldris.alderwick.highmead.house-of-the-ninth-angle',
    'location.caldris.prediction-court': 'location.caldris.alderwick.highmead.prediction-court',
}

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def rows(table):
    return [json.loads(line) for line in (SNAPSHOT / f'tables/{table}-0001.jsonl').read_text(encoding='utf-8').splitlines()]

def decode(value):
    if not isinstance(value, dict):
        return json.loads(value)
    path = (SNAPSHOT / value['object']).resolve()
    assert path.is_relative_to(SNAPSHOT.resolve())
    content = path.read_bytes()
    assert hashlib.sha256(content).hexdigest() == path.stem
    return json.loads(content)

def component_map(entity):
    return {entry['qualifiedTypeId']: entry['value'] for entry in entity['components']}

def replace_component(entity, identity, value):
    entity['components'] = [c for c in entity['components'] if c['qualifiedTypeId'] != identity]
    entity['components'].append({'qualifiedTypeId': identity, 'value': copy.deepcopy(value)})

def remap(value):
    if isinstance(value, str):
        return ALIASES.get(value, value)
    if isinstance(value, list):
        return [remap(item) for item in value]
    if isinstance(value, dict):
        return {ALIASES.get(key, key): remap(item) for key, item in value.items()}
    return value

def ordered(entities):
    done = {WORLD}
    result = []
    pending = list(entities.values())
    while pending:
        batch = sorted([e for e in pending if e['containment']['containerEntityId'] in done], key=lambda e: e['entityId'])
        if not batch:
            raise ValueError('Unclosed/cyclic containment: ' + ', '.join(e['entityId'] for e in pending[:10]))
        result.extend(batch)
        done.update(e['entityId'] for e in batch)
        pending = [e for e in pending if e['entityId'] not in done]
    return result

def packages(entities, relationships):
    # Entity creation + containment + each component consume separate effects.
    result, current, effects = [], [], 0
    for entity in entities:
        cost = 2 + len(entity['components'])
        if current and (len(current) >= 64 or effects + cost > 120):
            result.append({'format': 'dantesroleplay.world-package/1', 'rootEntityId': WORLD, 'entities': current, 'relationships': []})
            current, effects = [], 0
        current.append(entity)
        effects += cost
    if current:
        result.append({'format': 'dantesroleplay.world-package/1', 'rootEntityId': WORLD, 'entities': current, 'relationships': []})
    # Fresh installer cannot submit relationships-only manifests. Attach all
    # relationships to the batch that creates their latest endpoint, splitting
    # entity batches further when their combined effects require it.
    assigned = set()
    final = []
    existing = {WORLD}
    for package in result:
        pending_entities = package['entities']
        while pending_entities:
            take = min(len(pending_entities), 20)
            while take:
                group = pending_entities[:take]
                available = existing | {e['entityId'] for e in group}
                edges = [r for i,r in enumerate(relationships) if i not in assigned and r['fromEntityId'] in available and r['toEntityId'] in available]
                if len(edges) <= 64 and sum(2+len(e['components']) for e in group) + len(edges) <= 128:
                    break
                take -= 1
            if not take:
                raise ValueError('One entity activates more than one bounded relationship batch; use runtime sync for edges.')
            selected = {json.dumps(r,sort_keys=True) for r in edges}
            assigned.update(i for i,r in enumerate(relationships) if json.dumps(r,sort_keys=True) in selected)
            final.append({'format':'dantesroleplay.world-package/1','rootEntityId':WORLD,'entities':group,'relationships':edges})
            existing=available
            pending_entities=pending_entities[take:]
    assert len(assigned) == len(relationships), (len(assigned),len(relationships))
    return final

def build():
    opening = read(HERE / 'opening-state.json')
    opening_media = read(HERE / 'opening-media.json')
    atlas = read(ROOT / 'docs/world/caldris/maps/lore-atlas/atlas-gm.json')
    packet = read(ROOT / 'catalog/applications/dnd2024/assets/caldris/measure-of-mercy/asset-import-manifest.json')
    new_map_owners = {asset['ownerLocationId'] for asset in packet['assets'] if asset['kind'] == 'map'}
    opening_asset_owners = {asset['ownerLocationId'] for asset in packet['assets']}
    packet_positions = {asset['ownerLocationId'] for asset in packet['assets'] if asset['mapAnchor'] is not None}
    packet_positions.update(value['id'] for value in packet['locationCreates'])
    entities = {}
    for row in rows('system_ecs_entity'):
        identity=row[2]
        if row[1] != SPACE or row[6] is not None or not (identity == WORLD or '.caldris.' in identity):
            continue
        if identity == 'world.caldris.participation.actor.caldris.ganji':
            continue
        entities[identity] = {'entityId':identity,'name':row[3],'components':[],'containment':None}
    for row in rows('system_ecs_component'):
        if row[1] == SPACE and row[2] in entities:
            entities[row[2]]['components'].append({'qualifiedTypeId':row[3],'value':decode(row[6])})
    for row in rows('system_ecs_containment'):
        if row[1] == SPACE and row[2] in entities:
            entities[row[2]]['containment']={'containerEntityId':row[3],'slot':row[4]}

    root = entities.pop(WORLD)
    for c in opening_media['root']['components']:
        replace_component(root,c['qualifiedTypeId'],c['value'])
    for entity in opening['entities']:
        identity=entity['entityId']
        if identity in ALIASES:
            continue
        previous=entities.get(identity)
        if previous:
            values=component_map(previous)
            values.update(component_map(entity))
            entity={**copy.deepcopy(entity),'components':[{'qualifiedTypeId':k,'value':v} for k,v in values.items()]}
        entities[identity]=copy.deepcopy(entity)

    # The reviewed atlas supplies the complete place inventory and containment.
    for place in atlas['places'].values():
        identity=place['id']
        assert identity in entities,identity
        if identity in new_map_owners:
            continue
        entity=entities[identity]
        entity['containment']={'containerEntityId':place['parentId'] or WORLD,'slot':'location'}
        if identity not in opening_asset_owners:
            replace_component(entity,'game.core.world.location',{key:place[key] for key in ['kind','summary','visibility']} | {'status':'active'})
        if place.get('x') is not None and place.get('y') is not None:
            replace_component(entity,'game.core.world.map.anchor',{'x':place['x'],'y':place['y']})

    omitted_markers=[]
    for identity,entity in entities.items():
        if entity['containment'] is None:
            if identity.startswith(CAMPAIGN+'.arc.'):
                entity['containment']={'containerEntityId':CAMPAIGN,'slot':'arc'}
            elif identity.startswith(CAMPAIGN+'.chapter.'):
                entity['containment']={'containerEntityId':CAMPAIGN,'slot':'chapter'}
            else:
                raise ValueError('Missing authored containment: '+identity)
        entity['containment']=remap(entity['containment'])
        if entity['containment']['containerEntityId'] in new_map_owners and identity not in packet_positions:
            if 'game.core.world.map.anchor' in component_map(entity):
                omitted_markers.append(identity)
                entity['components']=[c for c in entity['components'] if c['qualifiedTypeId']!='game.core.world.map.anchor']
        entity['components']=remap(entity['components'])

    # Do not resurrect obsolete spell access omitted by the reviewed opening.
    pending={p['qualifiedTypeId'] for p in opening_media.get('pendingCharacterComponents',[])}
    ganji=entities['actor.caldris.ganji']
    ganji['components']=[c for c in ganji['components'] if c['qualifiedTypeId'] not in pending]
    all_ids=set(entities)|{WORLD}
    relationships=[]
    seen=set()
    for row in rows('system_ecs_relationship'):
        if row[1] != SPACE:
            continue
        left,right=ALIASES.get(row[2],row[2]),ALIASES.get(row[3],row[3])
        if left not in all_ids or right not in all_ids or not row[4].startswith(('game.','dnd2024.')):
            continue
        relationship={'fromEntityId':left,'toEntityId':right,'qualifiedKind':row[4],'value':remap(decode(row[5]))}
        key=(left,right,row[4])
        if key not in seen:
            relationships.append(relationship);seen.add(key)
    for relationship in opening['relationships']:
        relationship=remap(relationship)
        key=(relationship['fromEntityId'],relationship['toEntityId'],relationship['qualifiedKind'])
        if key not in seen:
            relationships.append(relationship);seen.add(key)

    # Resolve only referenced blob bytes from the committed capture or opening packet.
    media_by_hash={entry['sha256']:entry for entry in opening_media['media']}
    blob_sources={entry['sha256']:entry for entry in read(SNAPSHOT/'manifest.json')['externalBlobs']}
    def hashes(value):
        if isinstance(value,dict):
            if isinstance(value.get('sha256'),str) and value.get('mimeType','').startswith('image/'):
                yield value
            for child in value.values():yield from hashes(child)
        elif isinstance(value,list):
            for child in value:yield from hashes(child)
    for image in hashes([root,*entities.values()]):
        digest=image['sha256']
        if digest not in media_by_hash:
            source=blob_sources[digest]
            media_by_hash[digest]={'sourcePath':(SNAPSHOT/source['object']).relative_to(ROOT).as_posix(),'sha256':digest,'mediaType':image['mimeType'],'byteLength':source['bytes']}
    used_hashes={image['sha256'] for image in hashes([root,*entities.values()])}
    media=[{k:v for k,v in entry.items() if k in ['sourcePath','sha256','mediaType','byteLength']} for digest,entry in sorted(media_by_hash.items()) if digest in used_hashes]
    for entry in media:
        content=(ROOT/entry['sourcePath']).read_bytes()
        assert len(content)==entry['byteLength'] and hashlib.sha256(content).hexdigest()==entry['sha256']
    result=ordered(entities)
    return root,result,relationships,media,omitted_markers

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--repair-database',type=Path)
    parser.add_argument('--repair-output',type=Path)
    args=parser.parse_args()
    root,entities,relationships,media,omitted=build()
    parts=packages(entities,relationships)
    for index,part in enumerate(parts):write(HERE/f'world/part-{index+1:03}.json',part)
    template=read(ROOT/'catalog/applications/dnd2024/install/installation.template.json')
    runtime=next(s for s in template['stateSpaces'] if s['scope']=='runtime-state-space')
    runtime['root']={k:v for k,v in root.items() if k!='containment'}
    runtime['worldPackages']=[f'catalog/applications/dnd2024/install/caldris/world/part-{i+1:03}.json' for i in range(len(parts))]
    template['runtime']['environment']['Knowledge:LocalPlayer:CampaignId']=CAMPAIGN
    template['application']['sources'].append({'id':'dnd2024-extension.caldris-homebrew','allowedRootId':'installation-source','relativeRoot':'catalog/extensions/dnd2024/caldris-homebrew/content','includePattern':'**/*','trust':'trusted','precedence':10,'logicalIdentity':'caldris-homebrew-content'})
    template['application']['extensionPackages']=['catalog/extensions/dnd2024/caldris-homebrew/extension-package.json']
    template['application']['selectedExtensionIds']=['caldris-homebrew']
    template['media']=media
    write(HERE/'installation.template.json',template)
    if args.repair_database:
        assert args.repair_output
        prepare_repair(args.repair_database,args.repair_output,root,entities,relationships,media)
    print(json.dumps({'entities':len(entities),'components':sum(len(e['components']) for e in entities),'relationships':len(relationships),'places':sum('game.core.world.location' in component_map(e) for e in entities),'packages':len(parts),'media':len(media),'unreviewedMarkersOmitted':len(omitted)},indent=2))

def prepare_repair(database,output,root,entities,relationships,media):
    if output.exists() and any(output.iterdir()):
        raise ValueError('Repair output must be a new or empty directory so the original before-state and reviewed revisions cannot be overwritten.')
    with sqlite3.connect(database.resolve().as_uri()+'?mode=ro',uri=True) as connection:
        connection.row_factory=sqlite3.Row
        existing={r['Id']:dict(r) for r in connection.execute('SELECT * FROM system_ecs_entity WHERE StateSpaceId=? AND DeletedAtUtc IS NULL',(SPACE,))}
        components={(r['EntityId'],r['QualifiedTypeId']):dict(r) for r in connection.execute('SELECT * FROM system_ecs_component WHERE StateSpaceId=?',(SPACE,))}
        containments={r['ContainedEntityId']:dict(r) for r in connection.execute('SELECT * FROM system_ecs_containment WHERE StateSpaceId=?',(SPACE,))}
        links={(r['FromEntityId'],r['ToEntityId'],r['QualifiedKind']):dict(r) for r in connection.execute('SELECT * FROM system_ecs_relationship WHERE StateSpaceId=?',(SPACE,))}
    write(output/'live-before.json',{'entities':list(existing.values()),'components':list(components.values()),'containments':list(containments.values()),'relationships':list(links.values())})
    writes=[]
    for entity in [root,*entities]:
        identity=entity['entityId'];current=existing.get(identity)
        entry={'entityId':identity,'name':current['Name'] if current else entity['name'],'expectedRevision':current['Revision'] if current else 0,'components':[]}
        for component in entity['components']:
            previous=components.get((identity,component['qualifiedTypeId']))
            if previous is None:
                entry['components'].append({**component,'expectedRevision':0})
        parent=entity.get('containment')
        if current is None and parent:
            entry['containment']={**parent,'expectedRevision':0}
        elif parent and identity in containments and containments[identity]['ContainerEntityId'] in ALIASES:
            entry['containment']={**parent,'expectedRevision':containments[identity]['Revision']}
        if current is None or entry['components'] or 'containment' in entry:writes.append(entry)
    for identity in ALIASES:
        current=existing.get(identity);previous=components.get((identity,'game.core.world.location'))
        if not current or not previous:continue
        value=json.loads(previous['Data'])
        if value['status']=='archived':continue
        value['status']='archived'
        writes.append({'entityId':identity,'name':current['Name'],'expectedRevision':current['Revision'],'components':[{'qualifiedTypeId':'game.core.world.location','expectedRevision':previous['Revision'],'value':value}]})
    new_links=[{**r,'expectedRevision':0} for r in relationships if (r['fromEntityId'],r['toEntityId'],r['qualifiedKind']) not in links]
    write(output/'repair-input.json',{'applicationId':'dnd2024','stateSpaceId':SPACE,'rootEntityId':WORLD,'entities':writes,'relationships':new_links})
    write(output/'media.json',media)

if __name__=='__main__':main()
