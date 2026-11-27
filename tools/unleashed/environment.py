"""Reviewed source time-of-day with native Reflex lighting/sky donors."""
import json,pathlib,sys
NIGHT={'Nat12','Nat16','SM01','FS13'}
STADIUM_NIGHT={'SX05','SX08','SX12','SX17','FS02','FS03'}
INDOOR={'SX03','SX07','SX10','SX11','SX14','SX15','SX16','SX19','FS04','FS07','FS08','MC07'}
DUSK={'SX01','SX09','FS05','SM02','Nat13','MC03'}
OVERCAST={'SX06','SX20','FS01','FS10','MC01','Nat08','Nat11'}
def profile(track):
    mode='night' if track in NIGHT else 'stadium_night' if track in STADIUM_NIGHT else 'indoor' if track in INDOOR else 'dusk' if track in DUSK else 'day'
    donor='SX_KingstonB' if mode in ('night','stadium_night','indoor') else 'FMX_Dover' if mode=='dusk' else 'NAT_Concord'
    sky=None if mode!='day' else 'overcast' if track in OVERCAST else 'partly_cloudy'
    return dict(environment_mode=mode,environment_donor=donor,environment_sky=sky)

def upgrade(stage,base):
    from import_library import descriptors
    for folder in sorted((stage/'Assets/unleashed').iterdir()):
        path=folder/'level.json';info=json.loads(path.read_text());info.update(profile(info['source_id']));path.write_text(json.dumps(info,indent=2))
        for ext,data in descriptors(base,info['level']).items():(stage/'Database'/(info['level']+'.'+ext)).write_bytes(data)
if __name__=='__main__':upgrade(pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2]))
