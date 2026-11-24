import sys, json; sys.path.insert(0, '.')
from rig import *
from gl import G
from scipy.optimize import minimize
dom = np.array([jname[j] for j in BJ[np.arange(len(BJ)), BW.argmax(1)]])
def region(kind):
    torso = np.isin(dom, ['spine_01', 'spine_02', 'spine_03', 'clavicle_l', 'clavicle_r', 'upperarm_l', 'upperarm_r', 'lowerarm_l', 'lowerarm_r', 'neck_01'])
    legs = np.isin(dom, ['pelvis', 'thigh_l', 'thigh_r', 'calf_l', 'calf_r'])
    y, x, z = BP[:, 1], BP[:, 0], BP[:, 2]
    head = dom == 'Head'
    extra = {'foot': np.isin(dom, ['foot_l', 'ball_l']) | ((dom == 'calf_l') & (y < 0.3)),
             'headtop': head & (y > 1.74), 'ears': head & (np.abs(x) > 0.05) & (y > 1.62) & (y < 1.86), 'face': head & (z > 0.04) & (y > 1.58) & (y < 1.78),
             'chest': np.isin(dom, ['spine_02', 'spine_03']) & (y > 1.12)}
    if kind in extra: return extra[kind]
    return {'top': torso, 'pants': legs, 'suit': torso | legs}[kind]
def piece_points(i):
    g = G(f'{SKF}/{i}/m.bin'); return g, np.vstack([q[3] for q in g.prims()])
def fit(i, kind, arm0, s0=1.0, t0=(0, 0, 0), free_arm=True, leg0=0.0):
    g, P = piece_points(i); c = P.mean(0)
    reg = region(kind); P = P[::max(1, len(P) // 4000)]
    cache = {}
    def body_at(arm, leg):
        a = (round(arm, 1), round(leg, 1))
        if a not in cache:
            Bp, _ = lbs(BP, BJ, BW, skin_mats(posed(arm=a[0], leg=a[1])))
            R = Bp[reg][::2]; cache[a] = (R, cKDTree(R))
        return cache[a]
    def f(x):
        s = np.exp(x[0]); t = x[1:4]; arm = x[4] if free_arm else arm0; leg = x[5] if kind in ('pants', 'suit') else 0.0
        Q = (P - c) * s + c + t; R, tr = body_at(arm, leg)
        d1, _ = tr.query(Q); d2, _ = cKDTree(Q).query(R)
        return np.mean(np.minimum(d1, .12)) + np.mean(np.minimum(d2, .12))
    x0 = np.r_[np.log(s0), t0, arm0, leg0]
    r = minimize(f, x0, method='Nelder-Mead', options={'xatol': 1e-3, 'fatol': 1e-5, 'maxiter': 900, 'initial_simplex': np.array([x0] + [x0 + e for e in np.diag([.08, .04, .06, .04, 8, 4])])})
    s, t, arm = float(np.exp(r.x[0])), r.x[1:4].tolist(), float(r.x[4] if free_arm else arm0)
    return {'s': s, 'c': c.tolist(), 't': t, 'arm': arm, 'leg': float(r.x[5]) if kind in ('pants', 'suit') else 0.0, 'err': float(r.fun)}
if __name__ == '__main__':
    i, kind, arm = int(sys.argv[1]), sys.argv[2], float(sys.argv[3])
    s0 = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0; t0 = tuple(map(float, sys.argv[5].split(','))) if len(sys.argv) > 5 else (0, 0, 0)
    leg0 = float(sys.argv[6]) if len(sys.argv) > 6 else 0.0
    res = fit(i, kind, arm, s0, t0, kind in ('top', 'suit'), leg0); print(i, json.dumps({k: (round(v, 3) if isinstance(v, float) else [round(q, 3) for q in v]) for k, v in res.items()}))
    json.dump(res, open(f'{HERE}/fits/fit_{i}.json', 'w'))
