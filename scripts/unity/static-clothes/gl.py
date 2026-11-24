"""minimal glTF/glb reader: node world matrices, mesh primitives (world positions), skins"""
import json, struct, os, io
import numpy as np
from PIL import Image
CT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8, 5122: np.int16, 5120: np.int8}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
def quat(q):
    x, y, z, w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
def local(n):
    if 'matrix' in n: return np.array(n['matrix'], float).reshape(4, 4).T
    M = np.eye(4); R = quat(n.get('rotation', [0, 0, 0, 1])) * np.array(n.get('scale', [1, 1, 1]))[None, :]
    M[:3, :3] = R; M[:3, 3] = n.get('translation', [0, 0, 0]); return M
class G:
    def __init__(s, path):
        s.path = path; d = open(path, 'rb').read()
        if d[:4] == b'glTF':
            jl = struct.unpack('<I', d[12:16])[0]; s.J = json.loads(d[20:20 + jl]); s.bufs = [d[28 + jl:]]
        else:
            s.J = json.loads(d); s.bufs = [open(os.path.join(os.path.dirname(path), b['uri']), 'rb').read() for b in s.J['buffers']]
        N = s.J['nodes']; s.parent = {}
        for i, n in enumerate(N):
            for c in n.get('children', []): s.parent[c] = i
        s.W = [None] * len(N)
        def w(i):
            if s.W[i] is None: s.W[i] = (w(s.parent[i]) if i in s.parent else np.eye(4)) @ local(N[i])
            return s.W[i]
        for i in range(len(N)): w(i)
        s.byname = {n.get('name'): i for i, n in enumerate(N)}
    def acc(s, i):
        a = s.J['accessors'][i]; v = s.J['bufferViews'][a['bufferView']]; c = CT[a['componentType']]; nc = NC[a['type']]
        st = v.get('byteStride', 0); o = v.get('byteOffset', 0) + a.get('byteOffset', 0); it = np.dtype(c).itemsize * nc; b = s.bufs[v.get('buffer', 0)]
        if st and st != it:
            raw = np.frombuffer(b, np.uint8, count=st * a['count'], offset=o).reshape(a['count'], st)[:, :it]
            arr = np.frombuffer(raw.tobytes(), c).reshape(a['count'], nc)
        else: arr = np.frombuffer(b, c, count=a['count'] * nc, offset=o).reshape(a['count'], nc)
        if a.get('normalized') and c != np.float32: arr = arr.astype(np.float32) / np.iinfo(c).max
        return arr
    def prims(s):
        """(node index, mesh name, prim dict, world positions)"""
        out = []
        for i, n in enumerate(s.J['nodes']):
            if 'mesh' not in n: continue
            m = s.J['meshes'][n['mesh']]
            for p in m['primitives']:
                P = s.acc(p['attributes']['POSITION']).astype(float)
                out.append((i, m.get('name'), p, (np.c_[P, np.ones(len(P))] @ s.W[i].T)[:, :3]))
        return out
    def image(s, ti):
        im = s.J['images'][s.J['textures'][ti]['source']]
        if 'uri' in im: return Image.open(os.path.join(os.path.dirname(s.path), im['uri']))
        v = s.J['bufferViews'][im['bufferView']]; o = v.get('byteOffset', 0); return Image.open(io.BytesIO(s.bufs[v.get('buffer', 0)][o:o + v['byteLength']]))
