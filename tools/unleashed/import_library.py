#!/usr/bin/env python3
"""Convert a local Unleashed checkout into ordinary Reflex library files.

Each level owns separate terrain, scenery, collision and colour assets; there is
no RXPK, mod registration or custom-track mount. Run the regular library packer
on the resulting library. Source files are never modified.
"""
import argparse, configparser, ctypes as C, hashlib, json, pathlib, struct, sys
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[2]

def descriptors(base, level, donor="NAT_Concord"):
    sys.path.insert(0, str(ROOT/'_re/formats'))
    from bxml import Document
    from alive_install import encode_element
    import xml.etree.ElementTree as ET
    db=base/'Database'
    doc=Document((db/(donor+'.dx9.level')).read_bytes()).to_element()
    subs=doc.find('Subscenes')
    for child in list(subs): subs.remove(child)
    for name in ('Base','FreeRide'): ET.SubElement(subs,'Subscene',name=level+name,path='.')
    # The renderer's common terrain shader/material references are supplied by
    # the player's Reflex install. Source geometry comes from this level's assets.
    scene=(db/(donor+'.dx9.scene')).read_bytes()
    count=struct.unpack_from('<I',scene,4)[0]
    table=[struct.unpack_from('<256sII',scene,8+i*264) for i in range(count)]
    parts=[]
    for i,(name,_,at) in enumerate(table):
        name=name.split(b'\0')[0].decode()
        if name not in (donor+'Base',donor+'FreeRide'): continue
        end=table[i+1][2] if i+1<count else len(scene)
        parts.append((name.replace(donor,level),scene[at:end]))
    if len(parts)!=2: raise ValueError('Reflex template lacks Base/FreeRide sections')
    header=struct.pack('<II',0xc0fffeee,len(parts));at=8+264*len(parts)
    for name,data in parts: header+=struct.pack('<256sII',name.encode(),0,at);at+=len(data)
    empty=Document((db/(donor+'.dx9.database')).read_bytes()).to_element()
    empty.set('assetSource', donor)
    return {'dx9.level':encode_element(doc),'dx9.database':encode_element(empty),
            'dx9.scene':header+b''.join(data for _,data in parts)}

def convert(source, base, output, entry):
    sys.path[:0]=[str(source/'_re/tools'),str(source/'_re/formats')]
    import pak,trn,tdf
    track=entry['id'];level='UNL_'+track
    archive=source/'Game Files/data'/f'{track}.pak'
    p=pak.Pak(archive)
    try:
        raw=p.read(f'Data/Env/{track}/{track}.dx.trn');terrain=trn.parse(raw)
        course_path=f'data\\env\\{track}\\{track}.tdf'.lower()
        course=tdf.parse(p.read(course_path)) if course_path in p.by_name else None
        scene=p.read(f'Data/Env/{track}/{track}.scn').decode('latin1')
    finally:p.close()
    leaves=[n for n in terrain['nodes'] if n['level']==0]
    w=max(n['x'] for n in leaves)+17;h=max(n['z'] for n in leaves)+17
    heights=np.full((h,w),np.nan);cell=terrain['cell_size']
    for n in leaves:heights[n['z']:n['z']+17,n['x']:n['x']+17]=np.array(trn.node_heights(terrain,n)).reshape(17,17)*cell
    if not np.isfinite(heights).all():raise ValueError('Incomplete source height coverage')
    extent=np.array([(w-1)*cell,(h-1)*cell]);center=extent/2
    spacing=next((s for s in (2,4,8,16) if max(extent)<=2047*s),None)
    if spacing is None:raise ValueError('Source exceeds supported terrain extent')
    def sample(x,z):
        u=np.clip(np.asarray(x)/cell,0,w-1);v=np.clip(np.asarray(z)/cell,0,h-1)
        ix=np.minimum(u.astype(int),w-2);iz=np.minimum(v.astype(int),h-2);fx=u-ix;fz=v-iz
        return (heights[iz,ix]*(1-fx)+heights[iz,ix+1]*fx)*(1-fz)+(heights[iz+1,ix]*(1-fx)+heights[iz+1,ix+1]*fx)*fz
    half=1023.5*spacing;x=np.arange(2048)*spacing-half+center[0];z=half-np.arange(2048)*spacing+center[1]
    grid=sample(x[None,:],z[:,None]).astype('<f4')
    direction=[0,0,1]
    if course and 'start_gate' in course['gates']:
        gate=course['gates']['start_gate'];px,_,pz=gate.pos;direction=list(gate.normal)
    else:
        scn=configparser.ConfigParser(strict=False,interpolation=None);scn.read_string(scene)
        starts=[s for s in scn.sections() if 'start' in s.lower() and 'position' in scn[s]]
        if starts: px,_,pz=map(float,scn[starts[0]]['position'].split(','))
        else:
            px,_,pz=map(float,scn['SceneInfo']['DefaultStartPosition'].split(','))
            direction=list(map(float,scn['SceneInfo']['DefaultStartDirection'].split(',')))
    direction=np.array(direction,dtype=float);direction[1]=0
    if np.linalg.norm(direction)<.01:raise ValueError('Invalid authored start direction')
    direction/=np.linalg.norm(direction);spawn=[px-center[0],float(sample(px,pz))+2,pz-center[1]]
    from scenery import convert as scenery
    world,collision,report=scenery(source,center,track,float(max(extent)),output/'Assets/unleashed'/level/'vegetation.json',reflex_archive=base)
    lib=C.CDLL(str(source.resolve()/'native/build/libmxacore.dylib'))
    lib.mxa_trn_parse.argtypes=[C.c_void_p,C.c_int64];lib.mxa_trn_parse.restype=C.c_void_p
    lib.mxa_trn_color_rgba.argtypes=[C.c_void_p,C.c_int,C.c_int,C.c_void_p,C.c_int];lib.mxa_trn_close.argtypes=[C.c_void_p]
    ptr=lib.mxa_trn_parse(raw,len(raw))
    if not ptr:raise ValueError('Source terrain colour decode failed')
    try:
        pixels=np.empty((2048,2048,4),dtype=np.uint8)
        if lib.mxa_trn_color_rgba(ptr,2048,2048,pixels.ctypes.data,pixels.nbytes)!=pixels.nbytes:raise ValueError('Source colour decode failed')
    finally:lib.mxa_trn_close(ptr)
    cx=np.clip(np.rint(x/extent[0]*2047).astype(int),0,2047);cz=np.clip(np.rint(z/extent[1]*2047).astype(int),0,2047)
    colour=pixels[cz[:,None],cx[None,:]].tobytes()
    from racing import assets as race_assets
    race,race_error=race_assets(source,track,center,sample)
    info=dict(race_type=('Supercross' if track.startswith('SX') else 'National') if race else None,race_note=race_error,version=1,level=level,display_name=entry['name'],kind=entry['category'],source_id=track,
              source_sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),spacing=spacing,scenery=report,material_rules=report['materialRules'],
              limitations=['Full-race validation covers representative circuits, not every venue','Ecosystem collision and animated materials pending'])
    from environment import profile
    info.update(profile(track))
    files={f'Database/{level}.{ext}':data for ext,data in descriptors(base,level).items()}
    folder=f'Assets/unleashed/{level}/'
    from appearance import assets as appearance_assets
    files.update({folder+name:data for name,data in appearance_assets(source,track,spacing,colour).items()})
    files.update({folder+'terrain.rxheight':struct.pack('<4sIIf6f',b'RXM1',1,2048,spacing,*spawn,*direction)+grid.tobytes(),
                  folder+'scenery.rxscenery':world,folder+'collision.rxcollision':collision,
                  folder+'colour.rxcolour':struct.pack('<4sII',b'RXCM',1,2048)+colour,
                  folder+'level.json':json.dumps(info,indent=2).encode()})
    if race:files[folder+'race.rxrace']=race
    for path,data in files.items():
        dest=output/path;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
    import startgate
    gate=startgate.write(output/folder,scene,center,track)   # Reflex's start gate where the source's stood (race levels)
    if gate:info['start_gate']=gate;(output/folder/'level.json').write_text(json.dumps(info,indent=2))
    if track.startswith('SX') or track in [f'FS{i:02}' for i in range(1,9)]+['MC06','MC07','MC08']:
        from stadium_lights import write as write_lights
        write_lights(source,output/folder,track)
    return info

def main():
    a=argparse.ArgumentParser(description=__doc__);a.add_argument('source',type=pathlib.Path);a.add_argument('base',type=pathlib.Path);a.add_argument('output',type=pathlib.Path);a.add_argument('--track',action='append');args=a.parse_args()
    catalog=json.loads((pathlib.Path(__file__).with_name('tracks.json')).read_text())
    entries=[e for e in catalog if (args.source/'Game Files/data'/f"{e['id']}.pak").is_file() and (not args.track or e['id'] in args.track)]
    results=[];failed=[]
    for e in entries:
        print(f"Converting {e['id']}: {e['name']}",flush=True)
        try:results.append(convert(args.source,args.base,args.output,e));print('Converted',flush=True)
        except Exception as ex:failed.append(dict(id=e['id'],error=str(ex)));print(f'FAILED: {ex}',flush=True)
    args.output.mkdir(parents=True,exist_ok=True)
    (args.output/'unleashed-import-report.json').write_text(json.dumps(dict(converted=results,failed=failed),indent=2))
    return bool(failed)
if __name__=='__main__':raise SystemExit(main())
