import sys; sys.path.insert(0, '.')
from gl import G
import numpy as np
from PIL import Image, ImageDraw
def splat(views, out, size=300, span=2.2):
    """views: list of [(points Nx3, colour)] -> front (x,y) + side (z,y) panels per view"""
    W = size; im = Image.new('RGB', (W * 2 * len(views), W + 16), (255, 255, 255)); d = ImageDraw.Draw(im)
    for k, (title, layers) in enumerate(views):
        for side in (0, 1):
            ox = (2 * k + side) * W
            for P, col in layers:
                if len(P) == 0: continue
                h = P[:, 0] if side == 0 else P[:, 2]
                u = (ox + W / 2 + h / span * W).astype(int); v = (W - 4 - P[:, 1] / span * W).astype(int) + 16
                ok = (u >= ox) & (u < ox + W) & (v >= 16) & (v < W + 16)
                for a, b in zip(u[ok][::2], v[ok][::2]): im.putpixel((int(a), int(b)), col)
        d.text((2 * k * W + 4, 2), title, fill=(0, 0, 0))
    im.save(out)
def body():
    from rig import QO; R = G(f'{QO}/Male_Ranger.gltf'); return np.vstack([P for i, nm, p, P in R.prims()])
if __name__ == '__main__':
    B = body()[::3]
    ids = sys.argv[1:]
    views = []
    for i in ids:
        from rig import SKF; g = G(f'{SKF}/{i}/m.bin'); P = np.vstack([q[3] for q in g.prims()])
        views.append((f'{i} {P.min(0).round(2)} {P.max(0).round(2)}', [(B, (190, 190, 190)), (P[::max(1, len(P) // 6000)], (220, 30, 30))]))
    splat(views, 'v_' + '_'.join(ids) + '.png')
