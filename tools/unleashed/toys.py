"""The animated props of an Unleashed level: the .toy's [AnimatedToys] (the Oceanside ferris wheel, the stadiums' finish arch,
windmills, the mill wheel, swinging signs, bells, nooses, the tyre swing, the score-tower people).

The .scn never lists them: the game builds each from `Data\\Env\\<track>\\Anim\\<MCFName>.mcf` (SLTFile = the model, [MOTION n] = its
.mot files), places it by Position / LookVector / UpVector and plays InitialMotion from InitialTime (Open Unleashed's
native/src/scene_api.c toys_load, AnimatedToy::vf31 00405f70). A toy's vertices are stored in the frame of their node (one node per
vertex: rigid parts), so the converter writes one hidden RXSC group per (model, node) with the node's triangles and bakes the
motion, sampled with the game's own key math (_re/tools/mot.py), into `toys.rxtoys`:

    'RXTY' u32 version (1), u32 toys
    per toy:  str name, str model, u8 looping, f32 duration, f32 initial time, u32 samples, u32 parts
              per part:  str group (the RXSC group that holds its mesh)
              samples x parts x { f32 x, y, z;  f32 qx, qy, qz, qw }   the part's frame in level space (terrain-centred feet)
    (str = u16 length + UTF-8.) Sample k is at k x duration / (samples - 1); one sample = a prop that does not move.

Not converted: a toy's collision (HasCollision), sounds, DependentToy / SceneModel links, the [BirdSplines] flocks.
"""
import math, struct
import numpy as np

RATE = 30.0   # samples per second (the motions are keyed at 30)


def ini(text):
    """Sections and keys, case-insensitive, the first value of a repeated key (the game's Ini)."""
    secs = {}; cur = None
    for line in text.splitlines():
        line = line.split(';', 1)[0].strip()
        if line.startswith('[') and ']' in line: cur = secs.setdefault(line[1:line.index(']')].strip().lower(), {})
        elif '=' in line and cur is not None:
            k, v = line.split('=', 1); cur.setdefault(k.strip().lower(), v.strip())
    return secs


def vec(text, default):
    try:
        v = [float(x) for x in str(text).split(',')[:3]]
        return np.array(v) if len(v) == 3 and all(math.isfinite(x) for x in v) else np.array(default, dtype=float)
    except (TypeError, ValueError): return np.array(default, dtype=float)


def truth(text):
    c = str(text or '').strip().upper()
    if c[:1] in ('T', 'Y') or c.startswith('ON'): return True
    try: return int(c, 0) != 0
    except ValueError: return False


def quat_matrix(q):
    """am_m4_from_quat_4b3ed0: the rotation rows (row vectors) of a motion quaternion."""
    x, y, z, w = q
    x2, y2, z2 = x + x, y + y, z + z
    return np.array([[1 - (z2 * z + y2 * y), y2 * x + z2 * w, z2 * x - y2 * w],
                     [y2 * x - z2 * w, 1 - (z2 * z + x2 * x), z2 * y + x2 * w],
                     [z2 * x + y2 * w, z2 * y - x2 * w, 1 - (y2 * y + x2 * x)]])


def matrix_quat(rows):
    """Unit quaternion (x, y, z, w) of a rotation given as rows (row vectors): the inverse of quat_matrix."""
    m = np.array(rows, dtype=float)
    for i in range(3):   # the frame without scale
        n = np.linalg.norm(m[i]); m[i] = m[i] / n if n > 1e-12 else np.eye(3)[i]
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1) * 2; q = [(m[1, 2] - m[2, 1]) / s, (m[2, 0] - m[0, 2]) / s, (m[0, 1] - m[1, 0]) / s, s / 4]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2]) * 2; q = [s / 4, (m[0, 1] + m[1, 0]) / s, (m[2, 0] + m[0, 2]) / s, (m[1, 2] - m[2, 1]) / s]
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2]) * 2; q = [(m[0, 1] + m[1, 0]) / s, s / 4, (m[1, 2] + m[2, 1]) / s, (m[2, 0] - m[0, 2]) / s]
    else:
        s = math.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1]) * 2; q = [(m[2, 0] + m[0, 2]) / s, (m[1, 2] + m[2, 1]) / s, s / 4, (m[0, 1] - m[1, 0]) / s]
    q = np.array(q); return q / np.linalg.norm(q)


def string(value):
    b = value.encode(); return struct.pack('<H', len(b)) + b


def convert(track, files, read, center, material, emit_draw, first_group):
    """(group names, toys.rxtoys bytes or None, report rows). Draws go out through emit_draw into groups first_group.."""
    import dxg, mot
    path = f'data/env/{track}/{track}.toy'.lower()
    if path not in files: return [], None, []
    toy = ini(read(path).decode('latin1'))
    try: count = int(float(toy.get('animatedtoys', {}).get('numberofanimatedtoys', '0') or 0))
    except ValueError: count = 0
    groups = []; models = {}; out = []; report = []
    folder = f'data/env/{track}/anim/'.lower()

    def parts_of(slt):
        """The model's rigid parts: {node: group name}, its nodes. One hidden RXSC group per node that owns triangles."""
        if slt in models: return models[slt]
        mesh = dxg.parse(read(folder + slt + '.dxg'))
        nodes = mesh['nodes']; palettes = mesh.get('palettes') or []; by_node = {}
        # the bind frames of the nodes in model space: a triangle is drawn by its first vertex's node, so a corner that belongs to
        # another node (a joint of the score-tower people) is carried into that node's frame as the bind pose places it
        bind = [None] * len(nodes)
        def bind_of(k, depth=0):
            if bind[k] is None:
                parent = nodes[k]['parent']; m = np.array(nodes[k]['matrix'], dtype=float).reshape(4, 4)
                bind[k] = m @ bind_of(parent, depth + 1) if 0 <= parent < len(nodes) and parent != k and depth < 64 else m
            return bind[k]
        for k in range(len(nodes)): bind_of(k)
        unbind = [np.linalg.inv(m) for m in bind]
        for surf in mesh['groups'][0]['surfaces']:
            quant = surf.get('quant', {})
            vertices = np.array(dxg.decode(*surf['attrs'][0], quant.get('pos_scale', 1)), dtype=float)
            uv = np.array(dxg.decode(*surf['attrs'][3], quant.get('uv_scale', 1))) if 3 in surf['attrs'] else np.zeros((len(vertices), 2))
            uv1 = np.array(dxg.decode(*surf['attrs'][10], quant.get('uv_scale', 1))) if 10 in surf['attrs'] else uv
            if uv1.shape != uv.shape: uv1 = uv
            colors = np.full((len(vertices), 4), 128, dtype=np.uint8); colors[:, 3] = 255
            if 2 in surf['attrs'] and surf['attrs'][2][0] == 0x40:
                packed = np.frombuffer(surf['attrs'][2][1], dtype='<u4')
                for channel, shift in enumerate((16, 8, 0)): colors[:, channel] = (packed >> shift) & 255
                colors[:, 3] = (packed >> 24) & 255
            # mxa_dxg_bones: the vertex's palette slot -> its node (none: node 0, as the reference's toy renderer)
            owner = np.zeros(len(vertices), dtype=int)
            if 4 in surf['attrs']:
                slot = np.array(dxg.decode(*surf['attrs'][4])).reshape(-1).astype(int)
                pi = surf.get('palette') or 0; palette = palettes[pi if pi < len(palettes) else 0] if palettes else None
                for i, s in enumerate(slot):
                    node = (palette[s] if 0 <= s < len(palette) else -1) if palette is not None else s
                    owner[i] = node if 0 <= node < len(nodes) else 0
            indices = np.array(dxg.strip_tris(surf['indices'], surf['nverts']), dtype=int).reshape(-1, 3)
            if not len(indices): continue
            mi = material(mesh['textures'][surf['material']])
            lead = owner[indices[:, 0]]   # a triangle goes with its first vertex's node
            for node in np.unique(lead):
                tris = indices[lead == node]; used = np.unique(tris); remap = np.full(len(vertices), -1); remap[used] = np.arange(len(used))
                points = vertices[used].copy()
                for other in np.unique(owner[used]):
                    if other == node: continue
                    take = owner[used] == other
                    points[take] = (np.c_[points[take], np.ones(take.sum())] @ bind[other] @ unbind[node])[:, :3]
                by_node.setdefault(int(node), []).append((mi, points, (uv[used], uv1[used]), remap[tris].reshape(-1).astype('<i4'), colors[used]))
        named = {}
        for node, draws in sorted(by_node.items()):
            name = 'UnleashedToy:%s:%d' % (slt, node); named[node] = name; index = first_group + len(groups); groups.append(name)
            for mi, vertices, uv, indices, colors in draws: emit_draw(mi, vertices, uv, indices, colors, index)
        models[slt] = (named, nodes); return models[slt]

    for i in range(1, count + 1):
        sec = toy.get('animatedtoy%d' % i)
        if not sec: continue
        name = sec.get('name', 'toy%d' % i); mcf = sec.get('mcfname', '').rsplit('.', 1)[0].lower()
        if not mcf or folder + mcf + '.mcf' not in files: report.append(dict(name=name, state='missing motion list ' + mcf)); continue
        general, motions = mot.parse_mcf(read(folder + mcf + '.mcf').decode('latin1'))
        slt = general.get('sltfile', '').rsplit('.', 1)[0].lower()
        if not slt or folder + slt + '.dxg' not in files: report.append(dict(name=name, state='missing model ' + slt)); continue
        named, nodes = parts_of(slt)
        if not named: report.append(dict(name=name, state='empty model ' + slt)); continue
        # MatrixToy::vf31: Mat_FromForwardUp(Position, LookVector, UpVector) on the model's root (as the .scn models are placed)
        pos = vec(sec.get('position'), (0, 0, 0)); pos[[0, 2]] -= center
        forward = vec(sec.get('lookvector'), (0, 0, 1)); forward /= np.linalg.norm(forward)
        up = vec(sec.get('upvector'), (0, 1, 0)); right = np.cross(up, forward); right /= np.linalg.norm(right); up = np.cross(forward, right)
        place = np.eye(4); place[0, :3] = right; place[1, :3] = up; place[2, :3] = forward; place[3, :3] = pos
        local = [np.array(n['matrix'], dtype=float).reshape(4, 4) for n in nodes]
        wanted = (sec.get('initialmotion') or 'none').strip().strip('"').lower(); motion = None; looping = False
        for _, mname, vue, loop, _ in motions:
            if mname.lower() == wanted and wanted != 'none':
                file = folder + vue.rsplit('.', 1)[0].lower() + '.mot'
                if file in files: motion, _ = mot.parse_mot(read(file), mname); looping = bool(loop)
                break
        try: t0 = float(sec.get('initialtime', '0') or 0)
        except ValueError: t0 = 0.0
        bone = {n['name'].upper(): k for k, n in enumerate(nodes)}

        def pose(t, states):
            """World frames of every node at motion time t (Character::vf34: rotations replace the node's rotation, positions its
            translation; a node the motion does not name keeps its bind frame)."""
            frames = [m.copy() for m in local]
            if motion is not None:
                for c, (b, keys) in enumerate(motion.rot):
                    node = bone.get(motion.bones[b].rstrip('\0').upper()) if b < len(motion.bones) else (b if b < len(nodes) else None)
                    if node is None: continue
                    q = mot.rot_eval(keys, motion.rot_type, t, states[c], motion.rot_prev[c])
                    if q is not None: frames[node][:3, :3] = quat_matrix(q)
                for c, (b, keys) in enumerate(motion.pos):
                    node = bone.get(motion.bones[b].rstrip('\0').upper()) if b < len(motion.bones) else (b if b < len(nodes) else None)
                    if node is None: continue
                    p = mot.pos_eval(keys, motion.pos_type, t, [*frames[node][3, :3], 1.0], motion.pos_prev[c])
                    frames[node][3, :3] = p[:3]
            world = [None] * len(nodes)
            def resolve(k, depth=0):
                if world[k] is None:
                    parent = nodes[k]['parent']
                    world[k] = frames[k] @ (resolve(parent, depth + 1) if 0 <= parent < len(nodes) and parent != k and depth < 64 else place)
                return world[k]
            return [resolve(k) for k in range(len(nodes))]

        duration = float(motion.duration) if motion is not None and motion.duration > 0 else 0.0
        steps = max(1, math.ceil(duration * RATE - 1e-4)) if duration > 0 else 0
        states = [mot.RotState() for _ in motion.rot] if motion is not None else []
        order = sorted(named); samples = []
        for k in range(steps + 1):
            world = pose(mot.f32(duration * k / steps) if steps else 0.0, states)
            row = []
            for node in order:
                m = world[node]
                if not np.isfinite(m).all(): raise ValueError('Invalid animated prop pose: ' + name)
                row.append((*m[3, :3], *matrix_quat(m[:3, :3])))
            samples.append(row)
        flat = np.array(samples, dtype='<f4')   # samples x parts x 7
        # keep neighbouring quaternions on one side (the client blends them linearly)
        for k in range(1, len(flat)):
            flip = np.einsum('ij,ij->i', flat[k, :, 3:], flat[k - 1, :, 3:]) < 0; flat[k, flip, 3:] *= -1
        moving = len(flat) > 1 and float(np.abs(flat - flat[0]).max()) > 1e-4
        if not moving: flat = flat[:1]
        out.append(string(name) + string(slt) + struct.pack('<Bffii', looping, duration if moving else 0.0, t0, len(flat), len(order))
                   + b''.join(string(named[node]) for node in order) + flat.tobytes())
        report.append(dict(name=name, model=slt, motion=wanted if motion is not None else 'none', looping=looping, duration=round(duration, 3),
                           parts=len(order), samples=len(flat), state='animated' if moving else 'placed (its motion moves no drawn part)' if motion is not None else 'placed (no motion)',
                           collision=truth(sec.get('hascollision'))))
    if not out: return groups, None, report
    return groups, struct.pack('<4sII', b'RXTY', 1, len(out)) + b''.join(out), report
