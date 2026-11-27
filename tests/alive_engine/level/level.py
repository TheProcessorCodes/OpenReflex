# level.py -- the calibration trims of the imported MX vs ATV Alive engine sets (core/plan/ALIVE_ENGINE_SOUNDS.md section 7): the A-weighted
# rms level the local rider hears from each Alive set and from the Reflex bike engine of its class, over a race-like mix of the engine
# states, from the libraries Dump.cs wrote (the events and waves exactly as the game builds them). numpy only.
#   python -I level.py <dump dir> [--ffmpeg F] [--json out.json]
# Per state: the state's event at AIFX 0 (the local rider: OriginalEngineAudio / AliveEngineAudio post AIFX 0 for the local human) with
# OriginalAudio.Tick's voice gain  inst.gain (event volume x (1 - U(0, volumeRand))) x si.gain x the range fades x the layer's volume
# envelopes x voice.gain (sound-def volume x U(volumeRandMin, 1)), as mean squares (the random factors' second moments); the event volume
# command (the trim) is 1. Pitch (event + sound-def pitch octaves, the pitch envelopes) shifts the spectrum before the A-weighting; the
# low-pass envelopes apply as a one-pole magnitude (field_mix.py's). The channel gain (Engine) is the same on both sides and left out.
# A state's waves: the sound definition's entries by their pick weights, each counted for its length (a random one-shot is heard for its
# length), each wave's A-weighted mean square from its power spectrum (tests/engine_audio/offline/loudness.py's method, the whole wave).
# The ride: RIDE below (the same state names on both sides; both state tables have every one of them). trim = Reflex level / Alive level,
# clamped to 0.25 .. 4.
import sys, os, json, math, argparse, subprocess, struct
import numpy as np

# 60 % full throttle (power, high), 25 % part throttle (med, low, half), 15 % idle / off the throttle; equal shares inside each group
RIDE = {'SustainPower': 0.30, 'SustainHigh': 0.30, 'SustainMed': 0.25 / 3, 'SustainLow': 0.25 / 3, 'SustainHalf': 0.25 / 3,
        'Idle': 0.075, 'DecelHigh': 0.075}
# the Reflex engine each Alive set is calibrated against (its class)
CLASS = {'MX_450_TRACK': 'MX_450F_01', 'MX_250F': 'MX_250F_01', 'MX_125R': 'MX_125R_01', 'MX_250R': 'MX_250R_TEST_05',
         'MX_400_TRACK': 'MX_450F_01', 'ATV_1000cc': 'MX_450F_01', 'MX_50cc': 'MX_125R_01'}
TRIM_MIN, TRIM_MAX = 0.25, 4.0
NFFT = 4096

def read_wav(path):
    b = open(path, 'rb').read(); p = 12; fmt = ch = rate = bits = 0; data = None
    while p + 8 <= len(b):
        cid = b[p:p + 4]; ln = struct.unpack('<I', b[p + 4:p + 8])[0]; body = p + 8
        if cid == b'fmt ': fmt, ch, rate, _, _, bits = struct.unpack('<HHIIHH', b[body:body + 16])
        elif cid == b'data': data = b[body:body + ln]; break
        p = body + ln + (ln & 1)
    assert fmt == 1 and bits == 16, (path, fmt, bits)
    a = np.frombuffer(data[:len(data) // (2 * ch) * 2 * ch], dtype='<i2').astype(np.float64) / 32768.0
    return a.reshape(-1, ch).mean(axis=1), rate      # Unity plays a 3D event's stereo wave folded to the source: its mean

def a_resp(f):
    f2 = f * f
    ra = (12194.0 ** 2 * f2 ** 2) / ((f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194.0 ** 2))
    return ra / 0.7943

class Wave:
    def __init__(self, x, rate):
        self.rate = rate; self.seconds = len(x) / rate; self.ms = float(np.mean(x ** 2)) if len(x) else 0.0
        if len(x) < NFFT: x = np.pad(x, (0, NFFT - len(x)))
        w = np.hanning(NFFT); P = np.zeros(NFFT // 2 + 1)
        for i in range(0, len(x) - NFFT + 1, NFFT // 2): P += np.abs(np.fft.rfft(x[i:i + NFFT] * w)) ** 2
        self.P = P; self.f = np.fft.rfftfreq(NFFT, 1.0 / rate)
    def a_ms(self, pitch=1.0, cutoff=None):
        """the A-weighted mean square played at `pitch` (the spectrum moves by the pitch), low-passed at `cutoff` Hz (one pole)."""
        tot = self.P.sum()
        if tot <= 0: return 0.0
        f = self.f * pitch; h = a_resp(f) ** 2
        if cutoff: h = h / (1 + (f / cutoff) ** 2)
        return self.ms * float((self.P * h).sum() / tot)

class Waves:
    def __init__(self, root, files, ffmpeg):
        self.root = root; self.files = files; self.ffmpeg = ffmpeg; self.cache = {}
    def get(self, key):
        if key not in self.cache:
            rel = self.files.get(key)
            if rel is None: self.cache[key] = None; return None
            path = os.path.join(self.root, rel)
            if path.endswith('.mp3'):   # the mpeg samples: decoded once next to the stream
                out = path[:-4] + '.dec.wav'
                if not os.path.exists(out):
                    subprocess.run([self.ffmpeg, '-nostdin', '-hide_banner', '-v', 'error', '-y', '-i', path, '-f', 'wav', '-acodec', 'pcm_s16le', out], check=True)
                path = out
            self.cache[key] = Wave(*read_wav(path))
        return self.cache[key]

def evaluate(env, v):
    x, y, sh = env['x'], env['y'], env['shape']; n = len(x)
    if n == 0: return 1.0
    if v <= x[0]: return y[0]
    for i in range(1, n):
        if v > x[i]: continue
        t = (v - x[i - 1]) / (x[i] - x[i - 1]) if x[i] > x[i - 1] else 1.0
        if sh[i] == 1: t = t * t * (3 - 2 * t)
        return y[i - 1] + (y[i] - y[i - 1]) * t
    return y[-1]

def dsp(env, y):
    lo, hi = env['dspMin'], env['dspMax']
    return lo * (hi / lo) ** y if env['log'] and lo > 0 and hi > 0 else lo + y * (hi - lo)

def norm(ev, p, aifx):
    if p < 0 or p >= len(ev['parameters']): return 0.0
    d = ev['parameters'][p]
    val = aifx if d['name'].lower() == 'aifx' else d['min']        # every other parameter at its initial value (FMOD: min)
    return min(1.0, max(0.0, (val - d['min']) / (d['max'] - d['min']))) if d['max'] > d['min'] else 0.0

def event_ms(ev, waves, aifx=0.0):
    """the event's A-weighted mean square at volume command 1 and its unweighted one; the waves that were not found."""
    vr = ev['volumeRand']
    inst2 = ev['volume'] ** 2 * (1 - vr + vr * vr / 3)                    # E[(volume (1 - U vr))^2]
    acc = 0.0; raw = 0.0; missing = []
    for layer in ev['layers']:
        v = norm(ev, layer['param'], aifx); vol = 1.0; pitch = 1.0; cutoff = None
        for env in layer['envelopes']:
            y = evaluate(env, norm(ev, env['param'] if env['param'] >= 0 else layer['param'], aifx))
            if env['kind'] == 'volume': vol *= y
            elif env['kind'] == 'pitch': pitch *= 2 ** (4 * (2 * y - 1))
            elif env['kind'] == 'lowpass':
                c = dsp(env, y); cutoff = c if c < 20000 else None
        for si in layer['instances']:
            if layer['param'] >= 0 and not (si['start'] - 1e-6 <= v <= si['start'] + si['length'] + 1e-6): continue
            rg = 1.0
            if layer['param'] >= 0:
                if si['fadeIn'] > 0: rg *= min(1.0, max(0.0, (v - si['start']) / si['fadeIn']))
                if si['fadeOut'] > 0: rg *= min(1.0, max(0.0, (si['start'] + si['length'] - v) / si['fadeOut']))
            sd = ev['defs'][si['def']]; m = min(sd['volumeRandMin'], 1.0)
            voice2 = sd['volume'] ** 2 * (1 + m + m * m) / 3                  # E[(sd volume U(m, 1))^2]
            g2 = inst2 * (si['gain'] * rg * vol) ** 2 * voice2
            ps = 2 ** (ev['pitchOctaves'] + sd['pitchOctaves']) * pitch
            wts = sd['weights'] if len(sd['weights']) == len(sd['files']) else [1.0] * len(sd['files'])
            num = den = rnum = 0.0
            for f, w in zip(sd['files'], wts):
                if not f: continue
                wv = waves.get(f)
                if wv is None: missing.append(f); continue
                t = w * wv.seconds / ps                                       # heard for its length at that pitch
                num += t * wv.a_ms(ps, cutoff); rnum += t * wv.ms; den += t
            if den > 0: acc += g2 * num / den; raw += g2 * rnum / den
    return acc, raw, missing

def plays(ev): return any(f for d in ev['defs'] for f in d['files'])

def ride(states, events, waves):
    """A state whose event plays no wave (or has no event) is left out and the others' shares grow to fill the ride: the 2-stroke Alive
    sets' HALF_SUSTAINED has no entry, and their state machines never enter SustainHalf (no gotostate or Goto_ marker names it)."""
    rows = {}; acc = raw = tot = 0.0; miss = []
    for st, w in RIDE.items():
        p = states.get(st)
        if p is None or p not in events or not plays(events[p]): rows[st] = None; continue
        a, r, m = event_ms(events[p], waves); miss += m
        rows[st] = (p, a, r); acc += w * a; raw += w * r; tot += w
    return (math.sqrt(acc / tot), math.sqrt(raw / tot), rows, miss) if tot > 0 else (0.0, 0.0, rows, miss)

def db(x): return 20 * math.log10(max(x, 1e-12))

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('dump'); ap.add_argument('--ffmpeg', default='ffmpeg'); ap.add_argument('--json')
    a = ap.parse_args()
    lib = json.load(open(os.path.join(a.dump, 'library.json'), encoding='utf-8'))
    waves = Waves(a.dump, lib['waves'], a.ffmpeg)
    out = {'ride': RIDE, 'reflex': {}, 'alive': {}}
    print('ride: ' + ', '.join('%s %.3f' % kv for kv in RIDE.items()))
    seen = set()
    for e in lib['reflex']['engines']:
        if e['project'] in seen: continue
        seen.add(e['project'])
        lv, rw, rows, miss = ride(e['states'], lib['reflex']['events'], waves)
        out['reflex'][e['project']] = {'level': lv, 'raw': rw, 'states': {k: v and {'event': v[0], 'a_rms': math.sqrt(v[1])} for k, v in rows.items()}}
        print('REFLEX %-16s A %.4f (%6.2f dBFS)  unweighted %.4f   %s%s' % (e['project'], lv, db(lv), rw,
              '  '.join('%s %.1f' % (k, db(math.sqrt(v[1]))) for k, v in rows.items() if v), ('  MISSING %d waves' % len(miss)) if miss else ''))
    for s in lib['alive']:
        lv, rw, rows, miss = ride(s['states'], s['events'], waves)
        ref = CLASS.get(s['project']); rl = out['reflex'].get(ref, {}).get('level')
        trim = min(TRIM_MAX, max(TRIM_MIN, rl / lv)) if rl and lv > 0 else 1.0
        out['alive'][s['project']] = {'level': lv, 'raw': rw, 'reflex': ref, 'reflex_level': rl, 'trim': trim, 'installed_trim': s['trim'],
                                      'states': {k: v and {'event': v[0], 'a_rms': math.sqrt(v[1])} for k, v in rows.items()}}
        print('ALIVE  %-16s A %.4f (%6.2f dBFS)  unweighted %.4f   %s%s' % (s['project'], lv, db(lv), rw,
              '  '.join('%s %.1f' % (k, db(math.sqrt(v[1]))) for k, v in rows.items() if v), ('  MISSING %d waves' % len(miss)) if miss else ''))
        if rl:
            r3 = round(trim, 2)
            print('       vs %-16s %.4f: %+.2f dB -> trim %.2f (%+.2f dB), after the trim %+.2f dB; installed trim %g -> %+.2f dB'
                  % (ref, rl, db(lv / rl), r3, db(r3), db(lv * r3 / rl), s['trim'], db(lv * s['trim'] / rl)))
    if a.json:
        with open(a.json, 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)

if __name__ == '__main__': main()
