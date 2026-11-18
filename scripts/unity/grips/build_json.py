# Writes Resources/Data/weapon_grips.json from manual.py (points measured on each model, glTF scene space) into GunFrame
# space (x mirrored for Unity; a model stored back to front is turned 180 deg), keeping the non-coordinate fields that
# are already in the file (leftHandStyle, holdBack, frameYaw, _about). Run from the folder holding manual.py: python3 build_json.py <json>
import json, sys
sys.path.insert(0, '.'); sys.path.insert(0, 'grips')
from manual import P
path = sys.argv[1]; doc = json.load(open(path))
keep = {w['look']: {k: v for k, v in w.items() if k in ('leftHandStyle', 'holdBack', 'frameYaw')} for w in doc['weapons']}
def unity(n, v, key):
    x, y, z = v; x = -x
    if P[n].get('flip'): x, z = -x, -z
    if key == 'leftHandGrip' and n.startswith('pistol'): x = 0.035
    return [round(x, 3) + 0.0, round(y, 3), round(z, 3)]
K = {'R': 'rightHandGrip', 'L': 'leftHandGrip', 'M': 'muzzle', 'A': 'aimPoint'}
prof = {n: {K[k]: unity(n, P[n][k], K[k]) for k in 'RLMA'} for n in P}
for c in doc['categories']: c.update(prof[c['name']])
for w in doc['weapons']:
    n = w['look']; base = prof[w['category']]
    for k in K.values(): w.pop(k, None)
    for k, v in prof[n].items():
        if any(abs(a - b) > 0.005 for a, b in zip(v, base[k])): w[k] = v
    w.update(keep.get(n, {}))
json.dump(doc, open(path, 'w'), indent=1); print('written', path)
