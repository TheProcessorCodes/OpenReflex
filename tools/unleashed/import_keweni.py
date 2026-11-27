#!/usr/bin/env python3
"""Import Keweni Falls terrain, static scenery, vegetation billboards, collision and race route.
Waterfall animation and ecosystem collision remain pending.
Uses the reference parsers in a local Open Unleashed checkout; never modifies it.
"""
import argparse, ctypes as C, gzip, hashlib, json, pathlib, struct, sys
import numpy as np


def convert(source, output):
    sys.path.insert(0, str(source / '_re/tools'))
    sys.path.insert(0, str(source / '_re/formats'))
    import pak, trn, tdf
    archive = source / 'Game Files/data/Nat17.pak'
    p = pak.Pak(archive)
    try:
        terrain = trn.parse(p.read('Data/Env/Nat17/Nat17.dx.trn'))
        course = tdf.parse(p.read('Data/Env/Nat17/Nat17.tdf'))
    finally:
        p.close()
    leaves = [n for n in terrain['nodes'] if n['level'] == 0]
    w = max(n['x'] for n in leaves) + 17
    h = max(n['z'] for n in leaves) + 17
    heights = np.full((h,w), np.nan)
    for n in leaves:
        heights[n['z']:n['z']+17,n['x']:n['x']+17] = np.array(trn.node_heights(terrain,n)).reshape(17,17) * terrain['cell_size']
    if not np.isfinite(heights).all(): raise ValueError('Incomplete terrain coverage')
    cell = terrain['cell_size']
    center = np.array([(w-1)*cell/2, (h-1)*cell/2])
    if max((w-1)*cell,(h-1)*cell)>8188: raise ValueError('Terrain exceeds Reflex extent')
    def sample(x,z):
        u=np.clip(np.asarray(x)/cell,0,w-1);v=np.clip(np.asarray(z)/cell,0,h-1)
        ix=np.minimum(u.astype(int),w-2);iz=np.minimum(v.astype(int),h-2)
        fx=u-ix;fz=v-iz
        return (heights[iz,ix]*(1-fx)+heights[iz,ix+1]*fx)*(1-fz)+(heights[iz+1,ix]*(1-fx)+heights[iz+1,ix+1]*fx)*fz
    spacing=4
    x=np.arange(2048)*spacing-4094+center[0];z=4094-np.arange(2048)*spacing+center[1]
    grid=sample(x[None,:],z[:,None]).astype('<f4')
    points=[]
    for section in course['sections'].values():
        for pt in section['points']:
            px,_,pz=pt['centre'];left=np.array(pt['left']);right=np.array(pt['right'])
            points.extend([px-center[0],float(sample(px,pz)),pz-center[1],float(np.linalg.norm(right-left)),0])
    gate=course['gates']['start_gate'];px,_,pz=gate.pos
    spawn=[px-center[0],float(sample(px,pz))+2,pz-center[1]]
    direction=np.array(gate.normal,dtype=float);direction[1]=0;direction/=np.linalg.norm(direction)
    meta=dict(format='reflex-mod-track',version=1,id='unleashed-keweni-falls',name='Keweni Falls (Experimental)',kind='National',course=points,experimental=True,source='Open Unleashed Nat17',limitations=['Terrain and course only; original textures, scenery, object collision and race rules pending'],sourceSha256=hashlib.sha256(archive.read_bytes()).hexdigest())
    from scenery import convert as scenery_convert
    scenery,collision,report=scenery_convert(source,center)
    meta['scenery']=report
    meta['limitations']=['Original ecosystem, terrain textures and race rules pending']
    lib=C.CDLL(str(source.resolve()/'native/build/libmxacore.dylib'))
    lib.mxa_trn_parse.argtypes=[C.c_void_p,C.c_int64];lib.mxa_trn_parse.restype=C.c_void_p
    lib.mxa_trn_color_rgba.argtypes=[C.c_void_p,C.c_int,C.c_int,C.c_void_p,C.c_int]
    lib.mxa_trn_close.argtypes=[C.c_void_p]
    p=pak.Pak(archive)
    try:raw_terrain=p.read('Data/Env/Nat17/Nat17.dx.trn')
    finally:p.close()
    ptr=lib.mxa_trn_parse(raw_terrain,len(raw_terrain))
    if not ptr:raise ValueError('Cannot decode terrain colour')
    try:
        pixels=np.empty((2048,2048,4),dtype=np.uint8)
        if lib.mxa_trn_color_rgba(ptr,2048,2048,pixels.ctypes.data,pixels.nbytes)!=pixels.nbytes:raise ValueError('Colour decode failed')
    finally:lib.mxa_trn_close(ptr)
    cx=np.clip(np.rint(x/((w-1)*cell)*2047).astype(int),0,2047)
    cz=np.clip(np.rint(z/((h-1)*cell)*2047).astype(int),0,2047)
    colour=pixels[cz[:,None],cx[None,:]].tobytes()
    surface=np.zeros((2048,2048,4),dtype=np.uint8);surface[:,:,3]=255
    from racing import payload
    race=payload(course,center,sample)
    meta['raceVersion']=1
    meta['limitations']=['Waterfall UV animation and ecosystem collision are not yet converted']
    encoded=json.dumps(meta,separators=(',',':')).encode()
    if len(encoded)>65536:raise ValueError('Metadata too large')
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('xb') as raw, gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=0) as f:
        f.write(struct.pack('<4sII',b'RXPK',4,len(encoded)));f.write(encoded)
        f.write(struct.pack('<4sIIf6f',b'RXM1',1,2048,spacing,*spawn,*direction));f.write(grid.tobytes())
        f.write(struct.pack('<4sI',b'SCNE',len(scenery)));f.write(scenery)
        f.write(struct.pack('<4sI',b'COLL',len(collision)));f.write(collision)
        surf=struct.pack('<4sII',b'RXSM',1,2048)+surface.tobytes()
        rgb=struct.pack('<4sII',b'RXCM',1,2048)+colour
        f.write(struct.pack('<4sI',b'SURF',len(surf)));f.write(surf)
        f.write(struct.pack('<4sI',b'COLR',len(rgb)));f.write(rgb)
        f.write(struct.pack('<4sI',b'RACE',len(race)));f.write(race)
    with gzip.open(output,'rb') as f:data=f.read()
    assert len(data)==12+len(encoded)+40+2048*2048*4+16+len(scenery)+len(collision)+16+len(surf)+len(rgb)+8+len(race)
    assert np.isfinite(grid).all()
    print(json.dumps(dict(output=str(output),sourceGrid=[w,h],cell=cell,spawn=spawn,coursePoints=len(points)//5,heightRange=[float(grid.min()),float(grid.max())],limitations=meta['limitations']),indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=pathlib.Path);p.add_argument('output',type=pathlib.Path)
    a=p.parse_args();convert(a.source,a.output)
