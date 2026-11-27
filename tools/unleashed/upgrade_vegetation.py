"""Rebuild regular-library scenery, source visibility, materials and vegetation."""
import argparse,json,pathlib,sys
import numpy as np

def upgrade(source,stage,track):
    sys.path[:0]=[str(source/'_re/tools'),str(source/'_re/formats')]
    import pak,trn
    from scenery import convert
    p=pak.Pak(source/'Game Files/data'/f'{track}.pak')
    try:
        t=trn.parse(p.read(f'Data/Env/{track}/{track}.dx.trn'));scene_text=p.read(f'Data/Env/{track}/{track}.scn').decode('latin1')
    finally:p.close()
    leaves=[n for n in t['nodes'] if n['level']==0]
    extent=np.array([max(n['x'] for n in leaves)+16,max(n['z'] for n in leaves)+16])*t['cell_size']
    folder=stage/'Assets/unleashed'/('UNL_'+track)
    scenery,collision,report=convert(source,extent/2,track,float(max(extent)),folder/'vegetation.json',reflex_archive=pathlib.Path(__file__).resolve().parents[2]/'game files/Archive')
    # Activity-only collision is removed with its hidden challenge meshes.
    (folder/'collision.rxcollision').write_bytes(collision)
    (folder/'scenery.rxscenery').write_bytes(scenery)
    import startgate
    gate=startgate.write(folder,scene_text,extent/2,track)   # (after the scenery: the source gate is left out of it for the same levels)
    info=json.loads((folder/'level.json').read_text());info['scenery']=report;info['material_rules']=report['materialRules'];info['start_gate']=gate
    (folder/'level.json').write_text(json.dumps(info,indent=2))
    return dict(track=track,ecosystem=True,**report)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=pathlib.Path);p.add_argument('stage',type=pathlib.Path);p.add_argument('--track',action='append');a=p.parse_args()
    results=[]
    for folder in sorted((a.stage/'Assets/unleashed').iterdir()):
        track=json.loads((folder/'level.json').read_text())['source_id']
        if a.track and track not in a.track:continue
        result=upgrade(a.source,a.stage,track);results.append(result);print(json.dumps(result),flush=True)
    (a.stage/'vegetation-upgrade-report.json').write_text(json.dumps(results,indent=2))
