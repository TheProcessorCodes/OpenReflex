"""Where Reflex's own start gate stands on an imported Unleashed race level: `startgate.rxgate`.

    'RXSG' u32 version (1), f32 x, y, z (the drop bar's hinge line, level space: terrain-centred feet), f32 dx, dy (0), dz (the riding direction)

A national's gate is the .scn's NatStartGate / NatDropGate, a supercross' THQsxgate / THQsxdropgate (and the *Split pair): the bar
stands where the source's drop gate model is, facing the way its gate model looks. Supermoto has no gate in the source: the bar goes
39 ft down the start chute (the riders then stand where they were staged before, 32 ft in). The client and the core keep the donor
level's TO_StartingGate, DropGate and start board ("30 seconds" girl) and move them there as one group (core/src/fmt_modtrack.h);
`scene_rules.hidden_models(scene, race_gate=True)` leaves the source's gate and its collision out of the scenery.
A level without the file starts as before (staged riders, the numeric countdown)."""
import configparser, math, struct
import numpy as np

GATES = ('natstartgate', 'thqsxgate', 'thqsxgatesplit')
DROPS = ('natdropgate', 'thqsxdropgate', 'thqsxdropgatesplit')
SOURCE_MODELS = GATES + DROPS


def races(track):
    """The levels that race from a gate: supercross, nationals, supermoto."""
    return track.startswith(('SX', 'Nat', 'SM'))


def ground(folder):
    """Height sampler of the level's own terrain.rxheight (as the core reads it)."""
    raw = (folder/'terrain.rxheight').read_bytes()
    spacing = struct.unpack_from('<f', raw, 12)[0]; grid = np.frombuffer(raw, '<f4', 2048*2048, 40).reshape(2048, 2048); half = 1023.5*spacing
    def sample(x, z):
        gx = min(2046.999, max(0, (x+half)/spacing)); gz = min(2046.999, max(0, (half-z)/spacing)); ix = int(gx); iz = int(gz); fx = gx-ix; fz = gz-iz
        return float((grid[iz, ix]*(1-fx)+grid[iz, ix+1]*fx)*(1-fz)+(grid[iz+1, ix]*(1-fx)+grid[iz+1, ix+1]*fx)*fz)
    return sample


LIFT = 0.75   # ft: the gate's steel base stands on the dirt (the donor's is that far above its own ground)


def seat(x, z, look, sample):
    """The bar's height: the gate's base (80 ft across, the slots 9 ft behind the bar) clear of the highest ground under it, so the
    base shows and the dropped bar does not sink into the surface."""
    side = (look[2], -look[0]); top = -1e9
    for across in np.linspace(-41, 41, 42):
        for along in np.linspace(-9, 2, 6):
            top = max(top, sample(x+side[0]*across+look[0]*along, z+side[1]*across+look[2]*along))
    return top + LIFT


def pose(scene_text, center, race, sample):
    """(position, direction, what it came from) or None."""
    scn = configparser.ConfigParser(strict=False, interpolation=None, allow_no_value=True); scn.read_string(scene_text)
    gate = drop = None
    for sec in scn.sections():
        obj = scn[sec]; name = (obj.get('slt') or '').strip().lower()
        if not sec.lower().startswith('model') or 'position' not in obj: continue
        if name in GATES and gate is None: gate = obj
        if name in DROPS and drop is None: drop = obj
    if gate is not None:
        at = [float(v) for v in (drop if drop is not None else gate)['position'].split(',')]
        look = np.array([float(v) for v in gate.get('lookvector', '0,0,1').split(',')]); look[1] = 0
        if np.linalg.norm(look) < .01: return None
        look /= np.linalg.norm(look); x = at[0]-center[0]; z = at[2]-center[1]
        return [x, seat(x, z, look, sample), z], look, 'the source gate ' + gate['slt']
    if race is None or len(race) < 100 or race[:4] != b'RXRA': return None
    g = struct.unpack_from('<6f', race, 20); look = np.array([g[3], 0, g[5]])
    if np.linalg.norm(look) < .01: return None
    look /= np.linalg.norm(look); x = g[0]+look[0]*39; z = g[2]+look[2]*39
    return [x, seat(x, z, look, sample), z], look, 'no source gate: 39 ft down the start chute'


def write(folder, scene_text, center, track):
    """Writes (or removes) the level's startgate.rxgate; returns the report entry."""
    out = folder/'startgate.rxgate'; race = (folder/'race.rxrace').read_bytes() if (folder/'race.rxrace').is_file() else None
    found = pose(scene_text, center, race, ground(folder)) if races(track) and race is not None else None
    if found is None:
        if out.exists(): out.unlink()
        return None
    at, look, why = found
    if not all(math.isfinite(v) for v in at): raise ValueError('Invalid start gate position')
    out.write_bytes(struct.pack('<4sI6f', b'RXSG', 1, *at, look[0], 0, look[2]))
    return dict(position=[round(v, 2) for v in at], direction=[round(float(look[0]), 4), round(float(look[2]), 4)], source=why)
