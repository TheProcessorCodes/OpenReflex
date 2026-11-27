"""Recover stadium lamp positions from the source's actual bulb geometry."""
import pathlib,sys,json,configparser
from functools import lru_cache
import numpy as np

@lru_cache(maxsize=1)
def library(source):
    sys.path[:0]=[str(source/'_re/tools'),str(source/'_re/formats')]
    import pak
    archives=[pak.Pak(p) for p in sorted((source/'Game Files/data').glob('*.pak'))]
    files={}
    for p in archives:
        for e in p.entries:files.setdefault(pathlib.PureWindowsPath(e.name).name.lower(),(p,e))
    return files

@lru_cache(maxsize=1024)
def lamp_parts(source,name):
    import dxg
    files=library(source);key=pathlib.PureWindowsPath(name).stem.lower()+'.dxg'
    p,e=files[key];m=dxg.parse(p.read(e));out=[]
    for s in m['groups'][0]['surfaces']:
        tex=m['textures'][s['material']].lower()
        if not any(k in tex for k in ('blub','bulb','light','lamp','fluor')) or any(k in tex for k in ('sign','leader','hole','support','pole')):continue
        q=s.get('quant',{});v=np.array(dxg.decode(*s['attrs'][0],q.get('pos_scale',1)))
        tri=dxg.strip_tris(s['indices'],s['nverts']);parent=list(range(len(v)))
        def root(i):
            while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
            return i
        for a,b,c in tri:parent[root(b)]=root(a);parent[root(c)]=root(a)
        groups={}
        for i in {j for t in tri for j in t}:groups.setdefault(root(i),[]).append(i)
        for ids in groups.values():out.append(v[ids].mean(0))
    return out

def write(source,folder,track):
    files=library(source)
    import trn,esb
    p,e=files[(track+'.scn').lower()];c=configparser.ConfigParser(strict=False);c.read_string(p.read(e).decode('latin1'))
    p,e=files[(track+'.dx.trn').lower()];raw=p.read(e);t=trn.parse(raw);leaves=[n for n in t['nodes'] if n['level']==0]
    center=np.array([max(n['x'] for n in leaves)+16,max(n['z'] for n in leaves)+16])*t['cell_size']/2
    sample,_,_=esb.terrain_sampler(raw);ground=float(sample(*center));lights=[]
    from scene_rules import hidden_models
    hidden,_,_=hidden_models(c)
    for sec,o in c.items():
        if 'slt' not in o or sec in hidden:continue
        pos=np.array([float(x) for x in o.get('position','0,0,0').split(',')]);pos[[0,2]]-=center
        f=np.array([float(x) for x in o.get('lookvector','0,0,1').split(',')]);f/=np.linalg.norm(f)
        up=np.array([float(x) for x in o.get('upvector','0,1,0').split(',')]);right=np.cross(up,f);right/=np.linalg.norm(right);rotation=np.array([right,np.cross(f,right),f])
        for local in lamp_parts(source,o['slt']):
            point=local@rotation+pos
            if point[1]<ground+12:continue
            if any(np.linalg.norm(point-np.array([x['x'],x['y'],x['z']]))<12 for x in lights):continue
            lights.append(dict(x=float(point[0]),y=float(point[1]),z=float(point[2]),ground=ground))
    (folder/'stadium-lights.json').write_text(json.dumps(dict(version=1,lights=lights)))
    return len(lights)

if __name__=='__main__':
    source=pathlib.Path(sys.argv[1]);stage=pathlib.Path(sys.argv[2]);library(source)
    for folder in sorted((stage/'Assets/unleashed').iterdir()):
        t=folder.name[4:]
        if t.startswith('SX') or t in [f'FS{i:02}' for i in range(1,9)]+['MC06','MC07','MC08']:print(t,write(source,folder,t),flush=True)
