"""Add authored closed races to an existing regular Unleashed library."""
import pathlib,sys,json
import numpy as np
from racing import assets

def upgrade(source,stage):
    sys.path[:0]=[str(source/'_re/tools'),str(source/'_re/formats')]
    import pak,trn,esb
    report=[]
    for folder in sorted((stage/'Assets/unleashed').iterdir()):
        info=json.loads((folder/'level.json').read_text());track=info['source_id']
        p=pak.Pak(source/'Game Files/data'/f'{track}.pak')
        try:raw=p.read(f'Data/Env/{track}/{track}.dx.trn')
        finally:p.close()
        terrain=trn.parse(raw);leaves=[n for n in terrain['nodes'] if n['level']==0]
        extent=np.array([max(n['x'] for n in leaves)+16,max(n['z'] for n in leaves)+16])*terrain['cell_size']
        sample,_,_=esb.terrain_sampler(raw)
        race,error=assets(source,track,extent/2,sample)
        info['race_type']=('Supercross' if track.startswith('SX') else 'National') if race else None
        info['race_note']=error
        if race:(folder/'race.rxrace').write_bytes(race)
        else:(folder/'race.rxrace').unlink(missing_ok=True)
        (folder/'level.json').write_text(json.dumps(info,indent=2))
        report.append(dict(track=track,race=info['race_type'],reason=error))
    (stage/'race-upgrade-report.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))
if __name__=='__main__':upgrade(pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2]))
