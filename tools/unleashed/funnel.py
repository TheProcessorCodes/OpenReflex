"""The funnel blockers of an Unleashed race level: the bales (supermoto / stadium trucks: barrels) that shape the start.

Source: the track's .aty `[FunnelBlockDown]` / `[FunnelBlockUp]` (SX01, SX03-SX20, SM03, MC07, MC08; Open Unleashed
_re/notes/pco_props.md "Funnel blockers"). Each names a physics collection (`CollectionName`: DownTripleBale / UpTripleBale,
Down_SMbarrell / UP_SMbarrell: every .scn model whose name starts with it), a trigger gate (TriggerPosition, TriggerLook,
TriggerWidth, TriggerHeight) and a move (MoveDirection, MoveRate ft/s, MoveDistance ft). The .scn places the Down set standing
and the Up set MoveDistance (20 ft) under the ground: that is the start layout. When a racer first passes the gate the Down set
sinks and the Up set rises, and the lap is closed off the start straight.

The converter keeps the two sets out of the static scenery: their meshes go into the RXSC groups `UnleashedFunnel:down` /
`UnleashedFunnel:up` at their .scn poses, and `funnel.rxfunnel` carries the gate, the moves and each set's collision triangles
(the models' `<model>f.col`, the mesh the original collides with):

    'RXFN' u32 version (1), f32 gate position x, y, z, f32 gate look x, y, z, f32 width, height, f32 rate (ft/s)
    2 x { f32 direction x, y, z, f32 distance, u32 vertices (3 per triangle), vertices x f32 x, y, z }   (0 = down, 1 = up; .scn pose)

Positions are level space (terrain-centred feet). A level without both sections has no file and its bales stay static scenery.
"""
import struct
import numpy as np
import toys

NAMES = ('down', 'up')


def number(text, default=0.0):
    try: return float(str(text).split(',')[0])
    except (TypeError, ValueError): return default


def sections(track, files, read):
    """[down, up] (each: name, direction, distance, rate, gate) or None."""
    path = f'data/env/{track}/{track}.aty'.lower()
    if path not in files: return None
    aty = toys.ini(read(path).decode('latin1')); out = []
    for key in ('funnelblockdown', 'funnelblockup'):
        s = aty.get(key)
        if not s or not s.get('collectionname'): return None
        direction = toys.vec(s.get('movedirection'), (0, 0, 0)); distance = number(s.get('movedistance')); rate = number(s.get('moverate'))
        look = toys.vec(s.get('triggerlook'), (0, 0, 1)); look[1] = 0
        if np.linalg.norm(direction) < .5 or not 0 < distance <= 200 or not 0 < rate <= 1000 or np.linalg.norm(look) < .01: return None
        out.append(dict(name=s['collectionname'].strip().strip('"').lower(), direction=direction/np.linalg.norm(direction), distance=distance, rate=rate,
                        position=toys.vec(s.get('triggerposition'), (0, 0, 0)), look=look/np.linalg.norm(look),
                        width=number(s.get('triggerwidth'), 100), height=number(s.get('triggerheight'), 200)))
    return out


def which(config, model):
    """0 (down) / 1 (up) for a model of one of the two collections, else None."""
    if not config: return None
    low = model.strip().lower()
    for k, s in enumerate(config):
        if low.startswith(s['name']): return k
    return None


def triangles(collider, model, pos, rotation):
    """The model's collision mesh (<model>f.col) at its scene pose: n x 3 points, 3 per triangle (empty without the file)."""
    try: shape = collider(model + 'f')
    except FileNotFoundError: return np.zeros((0, 3))
    out = []
    for part in shape.get('children', [shape]):
        if 'verts' not in part: continue
        vertices = np.frombuffer(part['verts'], '<i2' if part['compressed'] else '<f4').reshape(-1, 3).astype(float)
        matrix = np.array(part['matrix']).reshape(4, 3)
        vertices = (vertices @ matrix[:3] + matrix[3]) @ rotation + pos
        indices = np.array([t[4:7] for t in part['tris']], dtype=int)
        if len(indices): out.append(vertices[indices].reshape(-1, 3))
    return np.concatenate(out) if out else np.zeros((0, 3))


def pack(config, center, tris):
    """funnel.rxfunnel from the sections and each set's collision points."""
    gate = config[1]; pos = gate['position'].copy(); pos[[0, 2]] -= center
    out = struct.pack('<4sI9f', b'RXFN', 1, *pos, *gate['look'], gate['width'], gate['height'], min(config[0]['rate'], config[1]['rate']))
    for k in range(2):
        points = np.concatenate(tris[k]).astype('<f4') if tris[k] else np.zeros((0, 3), '<f4')
        if not np.isfinite(points).all() or len(points) > 60000: raise ValueError('Invalid funnel collision')
        out += struct.pack('<4fI', *config[k]['direction'], config[k]['distance'], len(points)) + points.tobytes()
    return out
