import sys; sys.path.insert(0, '.')
from gl import G, quat
import numpy as np
from scipy.spatial import cKDTree
import os
QO = os.environ.get('LW_QO', 'qo')          # Quaternius 'Modular Character Outfits - Fantasy': Male_Ranger.gltf, Head_Male.glb, hair/
BASE = os.environ.get('LW_BASE', 'Superhero_Male_FullBody.gltf')  # Quaternius 'Universal Base Characters'
SKF = os.environ.get('LW_SKF', 'skf')        # the Sketchfab downloads: <SKF>/<n>/m.bin (.glb)
HERE = os.path.dirname(os.path.abspath(__file__))
RG = G(f'{QO}/Male_Ranger.gltf')
SK = RG.J['skins'][0]; JN = SK['joints']; NJ = len(JN)
IBM = RG.acc(SK['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
JW = np.array([RG.W[j] for j in JN])           # rest world matrices
jname = [RG.J['nodes'][j]['name'] for j in JN]; jidx = {n: k for k, n in enumerate(jname)}
# body proxy: every Ranger primitive, world positions + weights (rest = bind)
# (the bare base body - Quaternius Superhero_Male - carried onto this rig's proportions: its bind space -> Ranger joints)
SH = G(BASE)
SSK = SH.J['skins'][0]; SIBM = SH.acc(SSK['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
snames = [SH.J['nodes'][j]['name'] for j in SSK['joints']]; smap = np.array([jname.index(n) for n in snames])
BP, BJ, BW, BI = [], [], [], []
for i, nm, p, P in SH.prims():
    if SH.J['nodes'][i]['name'] != 'SuperHero_Male': continue
    BI.append(SH.acc(p['indices']).astype(np.int64).reshape(-1, 3) + sum(len(x) for x in BP))
    BUV = SH.acc(p['attributes']['TEXCOORD_0']).astype(float); BN0 = SH.acc(p['attributes']['NORMAL']).astype(float); BMATI = p.get('material')
    J = smap[SH.acc(p['attributes']['JOINTS_0']).astype(int)]; W = SH.acc(p['attributes']['WEIGHTS_0']).astype(float)
    S = JW[smap] @ SIBM  # superhero bind -> ranger rest, per superhero joint
    A = np.einsum('nk,nkij->nij', W, S[SH.acc(p['attributes']['JOINTS_0']).astype(int)])
    BP.append(np.einsum('nij,nj->ni', A, np.c_[P, np.ones(len(P))])[:, :3]); BJ.append(J); BW.append(W)
BP = np.vstack(BP); BJ = np.vstack(BJ); BW = np.vstack(BW) / np.vstack(BW).sum(1, keepdims=True); BI = np.vstack(BI)
children = {k: [] for k in range(NJ)}
par = {}
for k, j in enumerate(JN):
    pj = RG.parent.get(j)
    while pj is not None and pj not in JN: pj = RG.parent.get(pj)
    if pj is not None: par[k] = JN.index(pj); children[JN.index(pj)].append(k)
def subtree(k):
    out = [k]
    for c in children[k]: out += subtree(c)
    return out
def rotz(deg):
    a = np.radians(deg); R = np.eye(4); R[:2, :2] = [[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]]; return R
def rotx(deg):
    a = np.radians(deg); R = np.eye(4); R[1:3, 1:3] = [[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]]; return R
def posed(arm=0.0, leg=0.0, fore=0.0):
    """world joint matrices with the arms lowered by `arm` degrees (from the T-pose), legs spread out by `leg` degrees"""
    M = JW.copy()
    def turn(bone, R):
        k = jidx[bone]; piv = M[k][:3, 3].copy(); T = np.eye(4); T[:3, 3] = piv; Ti = np.eye(4); Ti[:3, 3] = -piv
        X = T @ R @ Ti
        for s in subtree(k): M[s] = X @ M[s]
    if arm: turn('upperarm_l', rotz(-arm)); turn('upperarm_r', rotz(arm))
    if fore: turn('lowerarm_l', rotz(-fore)); turn('lowerarm_r', rotz(fore))
    if leg: turn('thigh_l', rotz(leg)); turn('thigh_r', rotz(-leg))
    return M
def skin_mats(M): return M @ IBM   # per joint: bind space -> posed world
def lbs(P, J, W, S):
    A = np.einsum('nk,nkij->nij', W, S[J])
    return np.einsum('nij,nj->ni', A, np.c_[P, np.ones(len(P))])[:, :3], A
def transfer(P, S, k=6, only=None):
    """weights for points P (in the posed space S) from the nearest posed body points"""
    BPp, _ = lbs(BP, BJ, BW, S)
    sel = np.ones(len(BP), bool) if only is None else only
    tree = cKDTree(BPp[sel]); d, ii = tree.query(P, k=k); bj, bw = BJ[sel][ii], BW[sel][ii]
    wd = 1 / (d + 1e-3) ** 2; acc = np.zeros((len(P), NJ))
    for q in range(k):
        for c in range(4): np.add.at(acc, (np.arange(len(P)), bj[:, q, c]), bw[:, q, c] * wd[:, q])
    top = np.argsort(-acc, 1)[:, :4]; tw = np.take_along_axis(acc, top, 1); tw /= tw.sum(1, keepdims=True)
    return top, tw, d[:, 0]
def unpose(P, N, J, W, S):
    _, A = lbs(np.zeros_like(P), J, W, S); Ai = np.linalg.inv(A)
    R = np.einsum('nij,nj->ni', Ai, np.c_[P, np.ones(len(P))])[:, :3]
    Nn = None
    if N is not None:
        Nn = np.einsum('nij,nj->ni', Ai[:, :3, :3], N); Nn /= np.linalg.norm(Nn, axis=1, keepdims=True) + 1e-12
    return R, Nn
