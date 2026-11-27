"""Import source UI pictures and surface placement for Reflex's material pipeline."""
import ctypes as C
import pathlib
import struct
import json
import sys
import numpy as np


def assets(source, track, spacing, colour=None):
    catalog=json.loads(pathlib.Path(__file__).with_name('tracks.json').read_text())
    entry=next(e for e in catalog if e['id']==track)
    profiles=json.loads(pathlib.Path(__file__).with_name('terrain_profiles.json').read_text())
    style=profiles[entry['terrain_profile']]
    sys.path[:0] = [str(source/'_re/tools'), str(source/'_re/formats')]
    import pak, trn, tdf
    p = pak.Pak(source/'Game Files/data'/f'{track}.pak')
    try:
        t = trn.parse(p.read(f'Data/Env/{track}/{track}.dx.trn'))
        course_path=f'data\\env\\{track}\\{track}.tdf'.lower()
        course=tdf.parse(p.read(course_path)) if course_path in p.by_name else None
    finally:
        p.close()
    leaves = [n for n in t['nodes'] if n['level'] == 0]
    w = max(n['x'] for n in leaves)+17
    h = max(n['z'] for n in leaves)+17
    grid = np.zeros((h,w), np.uint8)
    for n in leaves:
        grid[n['z']:n['z']+17,n['x']:n['x']+17] = np.frombuffer(t['blocks'][n['geom']]['material'],np.uint8).reshape(17,17)&127
    extent = np.array([w-1,h-1])*t['cell_size']
    x = np.arange(2048)*spacing-1023.5*spacing+extent[0]/2
    z = 1023.5*spacing-np.arange(2048)*spacing+extent[1]/2
    ix = np.clip(np.rint(x/t['cell_size']).astype(int),0,w-1)
    iz = np.clip(np.rint(z/t['cell_size']).astype(int),0,h-1)
    ids = grid[iz[:,None],ix[None,:]]
    # template.cmt MaterialIDs are zero-based in the terrain: dirt=0, grass=3,
    # off-track mud=2, light/heavy mud=6/7, stone=9, pavement=1, ice=5.
    surface = np.zeros((2048,2048,4),np.uint8)
    surface[:,:,0] = np.where(np.isin(ids,[3,12]),255,0)
    surface[:,:,1] = np.where(np.isin(ids,[2,6,7]),255,0)
    surface[:,:,2] = np.where(np.isin(ids,[1,5,8,9]),255,0)
    # Most shipped TRNs mark the entire landscape as dirt (ID 0), even its
    # painted grass and cliffs. Recover that visual placement from the source
    # color only; none of these baked pixels are used as the new ground albedo.
    if colour is not None:
        rgb=np.frombuffer(colour,np.uint8).reshape(2048,2048,4)[:,:,:3].astype(np.float32)
        r,g,b=rgb[:,:,0],rgb[:,:,1],rgb[:,:,2]
        green=np.clip(np.minimum((g-r-2)/16,(g-b-5)/20),0,1)
        if style['name'] in ('woodland','dry_woodland','red_clay'):
            green=np.clip(np.minimum((g-r+5)/12,(g-b-4)/16),0,1)
        if style['cover']=='sparse_grass':
            # Do not mistake olive/brown sand for grass. Preserve visibly green oases.
            green=np.clip(np.minimum((g-r-8)/22,(g-b-12)/25),0,1)
        heights=np.zeros((h,w),np.float32)
        for n in leaves: heights[n['z']:n['z']+17,n['x']:n['x']+17]=np.array(trn.node_heights(t,n)).reshape(17,17)*t['cell_size']
        dz,dx=np.gradient(heights,t['cell_size'])
        slope=np.sqrt(dx*dx+dz*dz)[iz[:,None],ix[None,:]]
        grey=1-np.clip((rgb.max(2)-rgb.min(2)-12)/35,0,1)
        rock=np.clip((slope-.65)/.8,0,1)*grey*(1-green)
        unknown=ids==0
        if style['cover']=='snow':
            lum=.299*r+.587*g+.114*b
            if style['name']=='snow':
                green=np.ones_like(g) # snow terrain, including shaded and groomed snow
                rock=np.zeros_like(g)
            else:
                neutral=np.clip(1-(rgb.max(2)-rgb.min(2))/np.maximum(lum*.45,1),0,1)
                green=np.clip((lum-45)/65,0,1)*neutral
                rock=np.clip((slope-1.6)/1.2,0,1)*(1-green)
            surface[:,:,3]=255 # cover layer is snow: keep its deformation soft
        elif style['name']=='supermoto':
            lum=.299*r+.587*g+.114*b
            neutral=np.clip(1-(rgb.max(2)-rgb.min(2))/np.maximum(lum*.14,1),0,1)
            rock=neutral*np.clip((lum-25)/30,0,1)*(1-green)*np.clip((.6-slope)/.3,0,1)
        elif style['name'] in ('dunes','coastal'):
            # Dune crests are still sand; stone requires an explicitly authored material.
            rock=np.zeros_like(g)
        surface[:,:,0]=np.where(unknown,(green*255).astype(np.uint8),surface[:,:,0])
        surface[:,:,2]=np.where(unknown,(rock*255).astype(np.uint8),surface[:,:,2])
    result = {'terrain-style.json':json.dumps(style,indent=2).encode(), 'surface.rxsurface' :struct.pack('<4sII',b'RXSM',1,2048)+surface.tobytes()}
    segments=[]
    if course:
        for section in course['sections'].values():
            for a,b in zip(section['points'],section['points'][1:]):
                for point in (a,b):
                    px,_,pz=point['centre']
                    width=np.linalg.norm(np.array(point['left'])-point['right'])
                    segments.extend([float(px-extent[0]/2),float(pz-extent[1]/2),float(np.clip(width*.3,3,18))])
    result['ruts.json']=json.dumps({'segments':segments}).encode()
    lib = C.CDLL(str(source/'native/build/libmxacore.dylib'))
    lib.mxa_dxt_parse.argtypes=[C.c_void_p,C.c_int64];lib.mxa_dxt_parse.restype=C.c_void_p
    lib.mxa_dxt_info_get.argtypes=[C.c_void_p,C.c_void_p]
    lib.mxa_dxt_decode_rgba.argtypes=[C.c_void_p,C.c_int,C.c_void_p,C.c_int]
    lib.mxa_dxt_close.argtypes=[C.c_void_p]
    ui = pak.Pak(source/'Game Files/data/UI.pak')
    try:
        entries={e.name.lower().replace('\\','/'):e for e in ui.entries}
        for kind, suffixes in [('preview',['_preview_01','_preview_1','_preview','_lobby']),('map',['_map','_map_ss'])]:
            candidates=[f'data/ui/tex/{track.lower()}{s}.dxt' for s in suffixes]
            if kind=='preview': candidates += [f'data/ui/tex/ss_{track.lower()}_lobby.dxt',f'data/ui/tex/store_{track.lower()}.dxt']
            key=next((k for k in candidates if k in entries),None)
            if key is None:
                if kind=='map': continue
                raise ValueError('No source preview for '+track)
            raw=ui.read(entries[key]);ptr=lib.mxa_dxt_parse(raw,len(raw))
            if not ptr: raise ValueError('Invalid picture '+key)
            try:
                info=(C.c_int*8)();assert lib.mxa_dxt_info_get(ptr,info)==0
                w,h=info[2],info[3];rgba=C.create_string_buffer(w*h*4)
                assert lib.mxa_dxt_decode_rgba(ptr,0,rgba,len(rgba))==len(rgba)
                result[kind+'.rximage']=struct.pack('<4sIII',b'RXIM',1,w,h)+rgba.raw
            finally: lib.mxa_dxt_close(ptr)
    finally: ui.close()
    return result

if __name__=='__main__':
    import json
    source=pathlib.Path(sys.argv[1]);stage=pathlib.Path(sys.argv[2])
    for folder in sorted((stage/'Assets/unleashed').iterdir()):
        info=json.loads((folder/'level.json').read_text())
        for name,data in assets(source,info['source_id'],info['spacing'],(folder/'colour.rxcolour').read_bytes()[12:]).items(): (folder/name).write_bytes(data)
        print(folder.name,flush=True)
