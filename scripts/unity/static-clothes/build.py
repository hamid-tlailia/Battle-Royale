"""static Sketchfab clothing pieces -> a Quaternius-style outfit .gltf on the Male_Ranger rig (then import-modular-outfits.mjs)
each piece: the fit (scale / move, arm + leg angle the piece was modelled in, fit.py) -> weights from the nearest point of the
bare base body posed the same way -> unposed back to the rig's T-pose (inverse of its own skinning) -> named by part"""
import sys, json, os, io, struct; sys.path.insert(0, os.path.dirname(__file__))
from rig import *
from gl import G
from fit import piece_points, dom
from PIL import Image
LEGJ = {'pelvis', 'thigh_l', 'thigh_r', 'calf_l', 'calf_r', 'foot_l', 'foot_r', 'ball_l', 'ball_r'}
ARMJ = {n for n in jname if any(n.startswith(p) for p in ('upperarm', 'lowerarm', 'hand', 'clavicle', 'thumb', 'index', 'middle', 'ring', 'pinky'))}
HEADJ = {'Head', 'neck_01'}
def allowed(kind):
    """the body points a piece may take its weights from"""
    if kind in ('top', 'chest'): return ~np.isin(dom, list(LEGJ - {'pelvis'})) & ~np.isin(dom, list(HEADJ))
    if kind == 'pants': return np.isin(dom, list(LEGJ | {'spine_01'}))
    if kind == 'suit': return ~np.isin(dom, list(HEADJ))
    if kind == 'foot': return np.isin(dom, ['foot_l', 'ball_l', 'calf_l', 'foot_r', 'ball_r', 'calf_r'])  # (the mirrored boot: the right foot)
    return np.isin(dom, list(HEADJ))  # head pieces
def piece_prims(i):
    g = G(f'{SKF}/{i}/m.bin'); out = []
    for ni, nm, p, P in g.prims():
        W = g.W[ni]; Nm = np.linalg.inv(W[:3, :3]).T
        N = g.acc(p['attributes']['NORMAL']).astype(float) @ Nm.T if 'NORMAL' in p['attributes'] else None
        UV = g.acc(p['attributes']['TEXCOORD_0']).astype(float) if 'TEXCOORD_0' in p['attributes'] else np.zeros((len(P), 2))
        I = g.acc(p['indices']).astype(np.int64).reshape(-1) if 'indices' in p else np.arange(len(P))
        if np.linalg.det(W[:3, :3]) < 0: I = I.reshape(-1, 3)[:, ::-1].reshape(-1)
        mat = g.J['materials'][p['material']] if 'material' in p else {}
        pbr = mat.get('pbrMetallicRoughness', {}); sg = mat.get('extensions', {}).get('KHR_materials_pbrSpecularGlossiness', {}); img = None
        tex = pbr.get('baseColorTexture') or sg.get('diffuseTexture'); col = pbr.get('baseColorFactor') or sg.get('diffuseFactor') or [1, 1, 1, 1]
        if tex: img = g.image(tex['index']).convert('RGB')
        if img is not None and tuple(col[:3]) != (1, 1, 1): img = Image.fromarray((np.asarray(img, np.float32) * np.array(col[:3])).clip(0, 255).astype(np.uint8))
        out.append(dict(P=P, N=N, UV=UV, I=I, img=img, col=col, name=mat.get('name', 'm'), key=(i, p.get('material'))))
    return out
SleeveOver = 0.02   # a long sleeve's cuff past the wrist (m) (3.5 cm: the hand's skin, wider there, showed jagged through it)
WeightSmooth = 0    # passes of weight smoothing over the mesh (0: off - split seam vertices took different weights and the seams opened)
def rig_piece(i, kind, mirror=False, k=6):
    F = json.load(open(f'{HERE}/fits/fit_{i}.json')); c = np.array(F['c'])
    S = skin_mats(posed(arm=F['arm'], leg=F.get('leg', 0)))
    BPp, _ = lbs(BP, BJ, BW, S); al = allowed(kind)
    tree = cKDTree(BPp[al]); bj_all, bw_all = BJ[al], BW[al]
    out = []
    for pr in piece_prims(i):
        Q = (pr['P'] - c) * F['s'] + c + np.array(F['t']); N = pr['N']; I = pr['I']
        if mirror: Q = Q * [-1, 1, 1]; N = None if N is None else N * [-1, 1, 1]; I = I.reshape(-1, 3)[:, ::-1].reshape(-1)
        # (tops / suits: more body points, a softer fall-off - smoother weights over loose cloth, the same for the copies
        # of a vertex along a seam; spikes tore out behind the shoulder with the arm forward)
        kk, eps = (16, 0.02) if kind in ('top', 'chest', 'suit') else (k, 5e-3)
        d, ii = tree.query(Q, k=kk); bj, bw = bj_all[ii], bw_all[ii]
        wd = 1 / (d + eps) ** 2; acc = np.zeros((len(Q), NJ))
        for q in range(kk):
            for cc in range(4): np.add.at(acc, (np.arange(len(Q)), bj[:, q, cc]), bw[:, q, cc] * wd[:, q])
        if kind in ('top', 'chest', 'suit'):
            # a sleeve never follows the hand: what the hand / finger bones held goes to the forearm (a cuff bound to the
            # wrist turned with the hand round a gun's grip - dark flaps, the forearm bare)
            for sd in 'lr':
                for n in jname:
                    if n == f'hand_{sd}' or (n.endswith(f'_{sd}') and n.split('_')[0] in ('thumb', 'index', 'middle', 'ring', 'pinky')):
                        acc[:, jidx[f'lowerarm_{sd}']] += acc[:, jidx[n]]; acc[:, jidx[n]] = 0
        if kind in ('top', 'chest', 'suit') and WeightSmooth > 0:
            # the weights smoothed over the cloth's own neighbours (each vertex half toward its neighbours' mean, a few
            # times): next to each other two vertices took far apart bones and the cloth tore into spikes behind the
            # shoulder as the arm came forward
            acc = acc / acc.sum(1, keepdims=True)
            tri = I.reshape(-1, 3); nb = len(Q)
            rows = np.concatenate([tri[:, 0], tri[:, 1], tri[:, 2], tri[:, 1], tri[:, 2], tri[:, 0]])
            cols = np.concatenate([tri[:, 1], tri[:, 2], tri[:, 0], tri[:, 0], tri[:, 1], tri[:, 2]])
            from scipy.sparse import coo_matrix
            A = coo_matrix((np.ones(len(rows)), (rows, cols)), shape=(nb, nb)).tocsr(); A.data[:] = 1.0
            deg = np.asarray(A.sum(1)).ravel(); has = deg > 0
            for _ in range(WeightSmooth):
                m = A @ acc; m[has] /= deg[has, None]
                acc[has] = acc[has] * 0.5 + m[has] * 0.5
        top = np.argsort(-acc, 1)[:, :4]; tw = np.take_along_axis(acc, top, 1); tw /= tw.sum(1, keepdims=True)
        R, Nr = unpose(Q, N, top, tw, S)
        if kind in ('top', 'chest', 'suit'):
            # the forearm of a sleeve along the forearm: the models' sleeves were made with the elbows bent, the forearm
            # turned forward - fitted on a straight arm they stood 4-20 cm in front of it at the wrist (hidden from the
            # front, plain as soon as the arm moved). The sleeve's own forearm line (through the middles of its slices) is
            # turned about the elbow onto the bone, each vertex by its share of the forearm, eased in from the elbow
            for sd in 'lr':
                lj = jidx[f'lowerarm_{sd}']; a = JW[lj][:3, 3]; b = JW[jidx[f'hand_{sd}']][:3, 3]; ab = b - a; L2 = ab @ ab
                wf = (tw * (top == lj)).sum(1); core = top[:, 0] == lj
                if core.sum() < 20: continue
                t = ((R - a) @ ab) / L2
                ease = lambda x: np.clip(x, 0, 1) ** 2 * (3 - 2 * np.clip(x, 0, 1))
                r = wf * ease(t / 0.3)  # (nothing at the elbow, all of it from a third of the way down)
                mids = [R[core & (np.abs(t - bc) < 0.08)].mean(0) for bc in np.linspace(0.25, 1.0, 7) if (core & (np.abs(t - bc) < 0.08)).sum() >= 6]
                if len(mids) >= 5 and t[core].max() >= 0.8:
                    # a long sleeve: turned (a short one's line is not clear - only moved, below)
                    mids = np.array(mids); u = np.linalg.lstsq(np.c_[np.ones(len(mids)), np.linspace(0, 1, len(mids))], mids - a, rcond=None)[0][1]
                    u = u / np.linalg.norm(u); v = ab / np.sqrt(L2); axis = np.cross(u, v); sn = np.linalg.norm(axis)
                    if u @ v > 0 and sn > 1e-4:
                        axis /= sn; th = np.arctan2(sn, u @ v) * r; p = R - a
                        cs, s2 = np.cos(th)[:, None], np.sin(th)[:, None]
                        R = a + p * cs + np.cross(axis, p) * s2 + np.outer(p @ axis, axis) * (1 - cs)
                        if Nr is not None: Nr = Nr * cs + np.cross(axis, Nr) * s2 + np.outer(Nr @ axis, axis) * (1 - cs)
                # then the whole forearm part moved by one amount onto the bone (no slicing: that sheared it)
                t = ((R - a) @ ab) / L2; sel = core & (t > 0.2)
                if sel.sum() >= 10:
                    off = (R[sel] - (a + np.outer(t[sel], ab))).mean(0)
                    R = R - np.outer(r, off)
        if kind in ('top', 'chest', 'suit'):
            # a long sleeve ends at the wrist (a centimetre or two short left the forearm showing as the hand bent)
            for sd, sg in (('l', 1), ('r', -1)):
                m = top[:, 0] == jidx[f'lowerarm_{sd}']
                if not m.any(): continue
                ex, wx = sg * JW[jidx[f'lowerarm_{sd}']][0, 3], sg * JW[jidx[f'hand_{sd}']][0, 3]
                end = (sg * R[m, 0]).max()
                if wx - 0.045 < end < wx + 0.01:
                    x = sg * R[:, 0]; a = m & (x > ex); tip = wx + SleeveOver
                    R[a, 0] = sg * (ex + (x[a] - ex) * (tip - ex) / (end - ex))
                    # (the cuff stays on the forearm: given a share of the hand it twisted into sharp folds round a grip)
        # (stray bits of the downloads far off the body - a 3 cm piece floated 1.5 m away on the arm: dropped)
        far = d[:, 0] > 0.3
        if far.any(): tri = I.reshape(-1, 3); I = tri[~far[tri].any(1)].reshape(-1)
        out.append(dict(pr, P=R, N=Nr, J=top, W=tw, I=I))  # (I: the mirrored copy's faces turned back - it was left out and the right boot was drawn inside out)
    return out
def split_suit(prims):
    """a whole suit -> (torso prims, legs prims): a triangle goes to the legs when its vertices hang mostly on the hips / legs"""
    legs = np.array([n in LEGJ for n in jname])
    T, L = [], []
    for pr in prims:
        lw = (pr['W'] * legs[pr['J']]).sum(1); tri = pr['I'].reshape(-1, 3); isleg = lw[tri].mean(1) > 0.5
        T.append(dict(pr, I=tri[~isleg].reshape(-1))); L.append(dict(pr, I=tri[isleg].reshape(-1)))
    return T, L
def hands():
    """the bare hands of the Quaternius Villager (Male_Peasant: made for this very rig, its fingers weighted for it) - the
    hands carried over from the base body bent wrongly round a gun's grip; past the wrist, on this rig's joint numbers"""
    PG = G(f'{QO}/Male_Peasant.gltf'); pj = [PG.J['nodes'][j]['name'] for j in PG.J['skins'][0]['joints']]
    remap = np.array([jidx[n] for n in pj])
    wrist = RG.W[RG.byname['hand_l']][0, 3] - 0.16   # T-pose: the arms along x; the hand and most of the forearm (a short sleeve ends 10-13 cm short of the wrist: from 9 cm a ring of nothing showed between them)
    out = []
    for ni, nm, p, P in PG.prims():
        if nm != 'Male_Peasant_Arms' or p.get('material') != 1: continue
        J = remap[PG.acc(p['attributes']['JOINTS_0']).astype(int)]; W = PG.acc(p['attributes']['WEIGHTS_0']).astype(float)
        I = PG.acc(p['indices']).astype(np.int64).reshape(-1, 3)
        keep = I[(np.abs(P[I, 0]) > wrist).all(1)].reshape(-1)
        # the forearm's skin under the sleeve follows the forearm, not the hand (bound to the hand it swung out of the sleeve
        # as the wrist bent round a grip - a bare forearm between the cuff and the hand); the hand takes over in the last 2 cm
        wx = RG.W[RG.byname['hand_l']][0, 3]; J = J.copy(); W = W.copy()
        for v in np.where(np.abs(P[:, 0]) < wx - 0.005)[0]:
            sd = 'l' if P[v, 0] > 0 else 'r'; la = jidx[f'lowerarm_{sd}']; arm = (la, jidx[f'upperarm_{sd}'])
            t = float(np.clip((wx - 0.005 - abs(P[v, 0])) / 0.02, 0, 1))
            if la not in J[v]: c0 = int(np.argmin(W[v])); J[v, c0] = la; W[v, c0] = 0.0
            moved = 0.0
            for c in range(4):
                if J[v, c] not in arm: moved += W[v, c] * t; W[v, c] *= 1 - t
            W[v, list(J[v]).index(la)] += moved; W[v] /= W[v].sum()
        img = PG.image(PG.J['materials'][1]['pbrMetallicRoughness']['baseColorTexture']['index']).convert('RGB')
        out.append(dict(P=P, N=PG.acc(p['attributes']['NORMAL']).astype(float), UV=PG.acc(p['attributes']['TEXCOORD_0']).astype(float), I=keep, J=J, W=W, img=img, col=[1, 1, 1, 1], name='skin', key=('hands',)))
    return out

def write(set_name, meshes, outdir):
    """meshes: [(mesh name, [prims])] -> <outdir>/<set_name>.gltf + .bin + textures; skeleton, skin = Male_Ranger's"""
    os.makedirs(outdir, exist_ok=True)
    J = json.load(open(f'{QO}/Male_Ranger.gltf')); base = open(f'{QO}/Male_Ranger.bin', 'rb').read()
    keepacc = J['skins'][0]['inverseBindMatrices']
    acc0 = J['accessors'][keepacc]; bv0 = J['bufferViews'][acc0['bufferView']]
    blob = bytearray(base[bv0.get('byteOffset', 0): bv0.get('byteOffset', 0) + bv0['byteLength']])
    J['bufferViews'] = [dict(buffer=0, byteOffset=0, byteLength=len(blob))]; J['accessors'] = [dict(acc0, bufferView=0, byteOffset=0)]
    J['skins'][0]['inverseBindMatrices'] = 0
    arm = next(i for i, n in enumerate(J['nodes']) if n['name'] == 'Armature')
    meshnodes = [i for i, n in enumerate(J['nodes']) if 'mesh' in n]
    for i in meshnodes: J['nodes'][i] = {'name': '_unused'}
    J['nodes'][arm]['children'] = [c for c in J['nodes'][arm]['children'] if c not in meshnodes]
    J['meshes'], J['materials'], J['textures'], J['images'], J['samplers'] = [], [], [], [], [{}]
    def add(arr, comp, typ, target=None, minmax=False):
        nonlocal blob
        while len(blob) % 4: blob += b'\0'
        b = np.ascontiguousarray(arr).tobytes(); J['bufferViews'].append(dict(buffer=0, byteOffset=len(blob), byteLength=len(b), **({'target': target} if target else {}))); blob += b
        a = dict(bufferView=len(J['bufferViews']) - 1, componentType=comp, count=len(arr), type=typ)
        if minmax: a['min'] = arr.min(0).tolist(); a['max'] = arr.max(0).tolist()
        J['accessors'].append(a); return len(J['accessors']) - 1
    texcache = {}
    def material(pr):
        key = pr.get('key') or id(pr['img'])
        if key in texcache: return texcache[key]
        m = {'name': f'{set_name}_{len(J["materials"])}', 'pbrMetallicRoughness': {'metallicFactor': 0, 'roughnessFactor': 0.85}}
        if pr['img'] is not None:
            fn = f'{set_name}_{len(J["images"])}.jpg'; im = pr['img']; im.thumbnail((1024, 1024)); im.save(f'{outdir}/{fn}', quality=88)
            J['images'].append({'uri': fn}); J['textures'].append({'source': len(J['images']) - 1, 'sampler': 0})
            m['pbrMetallicRoughness']['baseColorTexture'] = {'index': len(J['textures']) - 1}
        else: m['pbrMetallicRoughness']['baseColorFactor'] = pr['col']
        J['materials'].append(m); texcache[key] = len(J['materials']) - 1; return texcache[key]
    for mname, prims in meshes:
        P_ = []
        for pr in prims:
            if len(pr['I']) == 0: continue
            used = np.unique(pr['I']); remap = np.full(len(pr['P']), -1); remap[used] = np.arange(len(used))
            P = pr['P'][used].astype(np.float32); N = pr['N'][used].astype(np.float32) if pr['N'] is not None else None
            att = {'POSITION': add(P, 5126, 'VEC3', 34962, True), 'TEXCOORD_0': add(pr['UV'][used].astype(np.float32), 5126, 'VEC2', 34962),
                   'JOINTS_0': add(pr['J'][used].astype(np.uint16), 5123, 'VEC4', 34962), 'WEIGHTS_0': add(pr['W'][used].astype(np.float32), 5126, 'VEC4', 34962)}
            if N is not None: att['NORMAL'] = add(N, 5126, 'VEC3', 34962)
            P_.append({'attributes': att, 'indices': add(remap[pr['I']].astype(np.uint32), 5125, 'SCALAR', 34963), 'material': material(pr)})
        if not P_: continue
        J['meshes'].append({'name': mname, 'primitives': P_})
        J['nodes'].append({'name': mname, 'mesh': len(J['meshes']) - 1, 'skin': 0}); J['nodes'][arm]['children'].append(len(J['nodes']) - 1)
    J['buffers'] = [{'byteLength': len(blob), 'uri': f'{set_name}.bin'}]
    open(f'{outdir}/{set_name}.bin', 'wb').write(blob); json.dump(J, open(f'{outdir}/{set_name}.gltf', 'w'))
    print(set_name, [(m, sum(len(p['I']) // 3 for p in ps)) for m, ps in meshes])

def undersuit(part):
    """a thin dark undersuit from the bare body (1.5 cm inside it): no gap shows between a jacket and trousers, trousers
    and boots, or inside an open jacket. part: 'torso' (chest, back, arms to the wrist) or 'legs' (hips to the ankles)"""
    T = ['spine_01', 'spine_02', 'spine_03', 'clavicle_l', 'clavicle_r', 'upperarm_l', 'upperarm_r']
    L = ['pelvis', 'thigh_l', 'thigh_r', 'calf_l', 'calf_r']
    want = np.isin(dom, T if part == 'torso' else L)
    tri = BI[want[BI].all(1)]
    fn = np.cross(BP[tri[:, 1]] - BP[tri[:, 0]], BP[tri[:, 2]] - BP[tri[:, 0]]); vn = np.zeros_like(BP)
    for c in range(3): np.add.at(vn, tri[:, c], fn)
    vn /= np.linalg.norm(vn, axis=1, keepdims=True) + 1e-12
    P = BP - vn * np.where(np.isin(dom, ['spine_01', 'pelvis']), 0.04, 0.015)[:, None]  # (the waist band deeper: kept whole, see conform)
    return [dict(P=P, N=vn, UV=np.zeros((len(P), 2)), I=tri.reshape(-1), J=BJ, W=BW, img=None, col=[0.035, 0.037, 0.04, 1], name='under', key=('under',))]

def conform(under, cloth, lat_max=0.035, depth=0.08, waist=True):
    """undersuit / bare-skin triangles with cloth right over them (or through them) are never seen: dropped. What is
    left fills only the gaps the clothes leave (no moving: pushing points under the cloth left spikes)"""
    CP = np.vstack([c['P'][np.unique(c['I'])] for c in cloth if len(c['I'])]); tr = cKDTree(CP)
    for u in under:
        P, n = u['P'], u['N']; used = np.unique(u['I'])
        d, ii = tr.query(P[used], k=12)
        rel = CP[ii] - P[used][:, None, :]; along = np.einsum('ikj,ij->ik', rel, n[used])
        lat = np.linalg.norm(rel - along[..., None] * n[used][:, None, :], axis=2)
        hit = (along > -0.12) & (along < depth)
        cov = np.zeros(len(P), bool); cov[used] = ((lat < lat_max) & hit).any(1)
        if waist and len(P) == len(dom):
            # (the waist band - spine / pelvis - always kept, set 4 cm inside the body in undersuit(): a top's hem standing
            # off a pair of trousers left the background showing between them; that deep it never shows through cloth)
            cov &= ~np.isin(dom, ['spine_01', 'pelvis'])
        tri = u['I'].reshape(-1, 3); u['I'] = tri[cov[tri].sum(1) < 2].reshape(-1)
    return under
