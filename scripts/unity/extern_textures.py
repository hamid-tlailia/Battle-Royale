#!/usr/bin/env python3
"""
Move the images embedded in .glb models out into texture files next to the project, so Unity's own texture
importer handles them (per-platform compression: ASTC on Android). glTFast keeps embedded images as uncompressed
RGBA32 sub-assets; images referenced by URI that are project assets are used as they are (with their import settings).

  extern_textures.py convert [model.glb ...]   default: every glb in Resources/Models with embedded images,
                                                except the guns, lw_base.glb and soldier.glb (kept untouched)
  extern_textures.py restore [model.glb ...]   puts the image bytes back into the glb (undo)

- the image bytes are written unchanged (no re-encoding): Assets/LastWar/Textures/<model>/<index>_<role>.<png|jpg>
  role: c = colour (sRGB), n = normal map, l = other linear data (metallic-roughness, occlusion)
- the glb's JSON points each image at its file ("../../Textures/<model>/..."); the binary chunk is repacked without
  the image bytes; meshes, accessors, materials, animations are not changed
- restore re-embeds the files (byte for byte) and removes them
"""
import json, os, struct, sys, glob

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'unity', 'Assets', 'LastWar')
MODELS = os.path.join(ROOT, 'Resources', 'Models')
TEXTURES = os.path.join(ROOT, 'Textures')
SKIP = lambda f: f.startswith('gun_') or f in ('lw_base.glb', 'soldier.glb')


def read_glb(path):
    d = open(path, 'rb').read()
    assert d[:4] == b'glTF', path
    n = struct.unpack('<I', d[12:16])[0]
    j = json.loads(d[20:20 + n])
    binc = b''
    if len(d) > 20 + n:
        bl = struct.unpack('<I', d[20 + n:24 + n])[0]
        binc = d[28 + n:28 + n + bl]
    return j, binc


def write_glb(path, j, binc):
    js = json.dumps(j, separators=(',', ':')).encode()
    js += b' ' * ((4 - len(js) % 4) % 4)
    binc += b'\0' * ((4 - len(binc) % 4) % 4)
    total = 12 + 8 + len(js) + (8 + len(binc) if binc else 0)
    out = struct.pack('<4sII', b'glTF', 2, total) + struct.pack('<I4s', len(js), b'JSON') + js
    if binc:
        out += struct.pack('<I4s', len(binc), b'BIN\0') + binc
    open(path, 'wb').write(out)


def roles(j):
    """image index -> role letter from how the materials use it"""
    r = {}
    tex = j.get('textures', [])

    def mark(info, role):
        if not info or 'index' not in info: return
        t = tex[info['index']]
        src = t.get('source')
        if src is None: return
        old = r.get(src)
        # normal wins over linear, linear over colour (a shared image is imported for its strictest use)
        rank = {'c': 0, 'l': 1, 'n': 2}
        if old is None or rank[role] > rank[old]: r[src] = role

    for m in j.get('materials', []):
        p = m.get('pbrMetallicRoughness', {})
        mark(p.get('baseColorTexture'), 'c')
        mark(p.get('metallicRoughnessTexture'), 'l')
        mark(m.get('normalTexture'), 'n')
        mark(m.get('occlusionTexture'), 'l')
        mark(m.get('emissiveTexture'), 'c')
        sg = m.get('extensions', {}).get('KHR_materials_pbrSpecularGlossiness', {})
        mark(sg.get('diffuseTexture'), 'c')
        mark(sg.get('specularGlossinessTexture'), 'c')
        for ext in m.get('extensions', {}).values():
            if isinstance(ext, dict):
                for k, v in ext.items():
                    if k.endswith('Texture') and isinstance(v, dict) and k not in ('diffuseTexture', 'specularGlossinessTexture'):
                        mark(v, 'l')
    return r


def repack(j, binc, drop):
    """remove the bufferViews in `drop` from the binary chunk and renumber every reference"""
    views = j.get('bufferViews', [])
    keep = [i for i in range(len(views)) if i not in drop]
    remap = {}
    out = bytearray()
    new_views = []
    for i in keep:
        v = dict(views[i])
        off = v.get('byteOffset', 0)
        data = binc[off:off + v['byteLength']]
        while len(out) % 4: out.append(0)
        v['byteOffset'] = len(out)
        out += data
        remap[i] = len(new_views)
        new_views.append(v)
    j['bufferViews'] = new_views
    for a in j.get('accessors', []):
        if 'bufferView' in a: a['bufferView'] = remap[a['bufferView']]
        sp = a.get('sparse')
        if sp:
            sp['indices']['bufferView'] = remap[sp['indices']['bufferView']]
            sp['values']['bufferView'] = remap[sp['values']['bufferView']]
    for im in j.get('images', []):
        if 'bufferView' in im: im['bufferView'] = remap[im['bufferView']]
    if j.get('buffers'): j['buffers'][0]['byteLength'] = len(out)
    return bytes(out)


def convert(path):
    name = os.path.splitext(os.path.basename(path))[0]
    j, binc = read_glb(path)
    imgs = j.get('images', [])
    emb = [i for i, im in enumerate(imgs) if 'bufferView' in im]
    if not emb: return 0
    rl = roles(j)
    d = os.path.join(TEXTURES, name)
    os.makedirs(d, exist_ok=True)
    views = j['bufferViews']
    drop = set()
    moved = 0
    for i in emb:
        im = imgs[i]
        v = views[im['bufferView']]
        off = v.get('byteOffset', 0)
        data = binc[off:off + v['byteLength']]
        ext = 'png' if data[:8] == b'\x89PNG\r\n\x1a\n' else 'jpg' if data[:2] == b'\xff\xd8' else None
        if ext is None: continue  # (other formats stay embedded)
        fn = f'{i}_{rl.get(i, "c")}.{ext}'
        open(os.path.join(d, fn), 'wb').write(data)
        drop.add(im['bufferView'])
        del im['bufferView']
        im.pop('mimeType', None)
        im['uri'] = f'../../Textures/{name}/{fn}'
        im.setdefault('extras', {})['lwEmbeddedMime'] = 'image/png' if ext == 'png' else 'image/jpeg'
        moved += len(data)
    binc = repack(j, binc, drop)
    write_glb(path, j, binc)
    return moved


def restore(path):
    name = os.path.splitext(os.path.basename(path))[0]
    j, binc = read_glb(path)
    binc = bytearray(binc)
    back = 0
    for im in j.get('images', []):
        uri = im.get('uri', '')
        if not uri.startswith(f'../../Textures/{name}/'): continue
        f = os.path.join(TEXTURES, name, uri.split('/')[-1])
        data = open(f, 'rb').read()
        while len(binc) % 4: binc.append(0)
        j.setdefault('bufferViews', []).append({'buffer': 0, 'byteOffset': len(binc), 'byteLength': len(data)})
        binc += data
        im['bufferView'] = len(j['bufferViews']) - 1
        im['mimeType'] = im.get('extras', {}).pop('lwEmbeddedMime', 'image/png')
        if not im.get('extras'): im.pop('extras', None)
        del im['uri']
        os.remove(f)
        for meta in (f + '.meta',):
            if os.path.exists(meta): os.remove(meta)
        back += len(data)
    if j.get('buffers'): j['buffers'][0]['byteLength'] = len(binc)
    write_glb(path, j, bytes(binc))
    d = os.path.join(TEXTURES, name)
    if os.path.isdir(d) and not os.listdir(d): os.rmdir(d)
    return back


if __name__ == '__main__':
    mode = sys.argv[1] if len(sys.argv) > 1 else ''
    files = sys.argv[2:] or [f for f in sorted(glob.glob(os.path.join(MODELS, '*.glb'))) if not SKIP(os.path.basename(f))]
    if mode not in ('convert', 'restore'): sys.exit(__doc__)
    total = 0
    for f in files:
        f = f if os.path.exists(f) else os.path.join(MODELS, f)
        n = convert(f) if mode == 'convert' else restore(f)
        if n: print(f'{mode} {os.path.basename(f)}: {n / 1e6:.2f} MB of images')
        total += n
    print(f'{mode}: {total / 1e6:.1f} MB of images in total')
