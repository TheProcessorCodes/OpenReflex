"""Reviewed Reflex ecosystem substitutions, packed alongside each imported level.

Keep unmatched species, including tropical foliage and landmark geometry. The
native Reflex forest renderer supplies tree LODs, leaf cards, normals and lighting.
"""
import json,pathlib,sys,struct
from functools import lru_cache
ROOT=pathlib.Path(__file__).resolve().parents[2]

def plan(track):
    return json.loads(pathlib.Path(__file__).with_name('scenery_profiles.json').read_text()).get(track)

@lru_cache(maxsize=8)
def donor_assets(archive, donor):
    sys.path.insert(0,str(ROOT/'_re/formats'))
    from bxml import Document
    from scene_read import SceneDocument
    from package_extract import extract
    from bike_asset_export import material_strings
    class ForestDocument(SceneDocument):
        def value(self,item):
            if item.kind==9:
                assert item.ref+item.count*8<=len(self.blob)
                return list(struct.unpack_from('<'+str(item.count*2)+'f',self.blob,item.ref))
            return super().value(item)
    database=pathlib.Path(archive)/'Database'/(donor+'.dx9.database')
    root=Document(database.read_bytes()).to_element()
    available={(a.get('name'),a.get('type')):p.get('name') for p in root.iter('Package') for a in p.findall('Asset')}
    def read(name,kind):return extract(database,available[name,kind],name,kind)[0]
    forests=[name for name,kind in available if kind=='forest'];assert len(forests)==1
    raw=read(forests[0],'forest');doc=ForestDocument(raw)
    types=[doc.attributes_at(i) for i,n in enumerate(doc.nodes) if doc.strings[n.name]=='Type']
    assets={};textures=set()
    for i,n in enumerate(doc.nodes):
        if doc.strings[n.name]=='BillboardAtlas':textures.update(doc.attributes_at(i).values())
    for t in types:
        name=t['TreeAsset'];assets[(name,'tree')]=read(name,'tree')
        for key in ('BranchMaterial','FrondMaterial','LeafMaterial','3DLeafMaterial'):
            name=t.get(key)
            if not name or (name,'material') in assets:continue
            data=read(name,'material');assets[(name,'material')]=data
            refs=material_strings(data);textures.update(refs['textures'])
            for shader in refs['shaders']:
                name=shader.get('asset')
                if (name,'shader') in available:assets[(name,'shader')]=read(name,'shader')
    for name in textures:assets[(name,'texture')]=read(name,'texture')
    return raw,types,assets

def write(archive,folder,track,plants,species):
    selected=plan(track)
    # Reimports must remove resources from an earlier donor selection.
    for old in folder.glob('reflex-resource-*'):old.unlink()
    for name in ('reflex-forest.bin','reflex-vegetation.json'):(folder/name).unlink(missing_ok=True)
    if not selected:return set()
    raw,types,assets=donor_assets(str(archive.resolve()),selected['donor'])
    by_name={t['TreeAsset']:i for i,t in enumerate(types)}
    mapping={i:by_name[selected['species'][s['mesh'].lower()]] for i,s in enumerate(species) if s['mesh'].lower() in selected['species']}
    assert mapping,'Reviewed substitutions did not match '+track
    instances=[dict(p,species=mapping[p['species']]) for p in plants if p['species'] in mapping]
    folder.mkdir(parents=True,exist_ok=True)
    (folder/'reflex-forest.bin').write_bytes(raw)
    for (name,kind),data in assets.items():
        assert pathlib.Path(name).name==name and '/' not in name and '\\' not in name
        (folder/('reflex-resource-'+name+'.'+kind)).write_bytes(data)
    (folder/'reflex-vegetation.json').write_text(json.dumps(dict(version=1,donor=selected['donor'],instances=instances)))
    return set(mapping)
