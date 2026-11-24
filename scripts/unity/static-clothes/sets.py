"""1799danly's loose Sketchfab clothes (CC-BY, no skeleton) -> 9 outfits on the Quaternius man's rig (qm_*: their head /
top / pants / shoes mix with Ranger's and Villager's in the lobby).

Per piece (fits/fit_<n>.json, found by fit.py: scale, move, and the arm / leg angle it was modelled in - A-pose jackets,
legs-apart trousers - matched to the bare Quaternius base body carried onto the Ranger rig):
  the body posed the same way -> each cloth vertex takes the weights of the nearest body points -> unposed to the rig's
  T-pose (the inverse of its own skinning). Suits split into top / pants by where their vertices hang. Under the clothes
  a thin dark undersuit from the body fills what they leave open (jacket hem to trousers, open jackets, trouser hem to
  boots; covered triangles dropped), bare hands from the base body. One boot model, mirrored for the other foot.

  LW_QO=<Modular Character Outfits - Fantasy glTF dir> LW_BASE=<Superhero_Male_FullBody.gltf> LW_SKF=<downloads dir> \
    python3 -I scripts/unity/static-clothes/sets.py [<set> ...]            -> out/Male_<Set>.gltf + .bin + textures
  then: (a dir with out/* + Head_Male.glb + hair/)  SETS=scripts/unity/static-clothes/sets.json \
    node scripts/unity/import-modular-outfits.mjs <that dir>;  python3 scripts/unity/extern_textures.py convert <the glbs>
  re-fit a piece: python3 -I fit.py <n> <top|pants|suit|foot|headtop|ears|face|chest> <arm deg> [scale] [x,y,z] [leg deg]
Pieces (Sketchfab model numbers in CREDITS.md): 10 Gorka suit, 13 / 21 jumpsuits, 19 cyberpunk suit, 12 / 14 / 22 / 24 / 26
tops, 15 / 20 / 25 trousers, 17 vest, 18 boot, 9 cap, 11 headset, 16 mask (head pieces scaled out of the head by hand).
"""
import sys; sys.path.insert(0, '.')
from build import *
cache = {}
def R(i, kind, mirror=False):
    k = (i, kind, mirror)
    if k not in cache: cache[k] = rig_piece(i, kind, mirror)
    return cache[k]
H = hands()
boots = lambda: R(18, 'foot') + R(18, 'foot', True)
SETS = {
  'gorka':    dict(suit=10, head=(9, 'headtop', 'Cap')),
  'pilot':    dict(suit=13, head=(11, 'ears', 'Headset')),
  'neon':     dict(suit=19, head=(16, 'face', 'Mask')),
  'scout':    dict(suit=21),
  'street':   dict(top=[(24, 'top')], legs=15, head=(9, 'headtop', 'Cap')),
  'biker':    dict(top=[(14, 'top')], legs=20),
  'field':    dict(top=[(22, 'top')], legs=25, head=(11, 'ears', 'Headset')),
  'silver':   dict(top=[(26, 'top')], legs=15),
  'operator': dict(top=[(12, 'top'), (17, 'chest')], legs=25, head=(16, 'face', 'Mask')),
}
only = sys.argv[1:] or list(SETS)
for name in only:
    d = SETS[name]; X = 'Male_' + name.capitalize(); meshes = []
    if 'suit' in d:
        T, L = split_suit(R(d['suit'], 'suit'))
    else:
        T = []
        for i, kind in d['top']: T += R(i, kind)
        L = R(d['legs'], 'pants')
    cl = T + L + boots()
    meshes += [(f'{X}_Body', T + conform(undersuit('torso'), cl)), (f'{X}_Legs', L + conform(undersuit('legs'), T + L))]  # (not trimmed by the boots: the sock shows between them and the hem)
    meshes += [(f'{X}_Arms_Hands', H), (f'{X}_Feet_Boots', boots())]
    if 'head' in d: i, kind, nm = d['head']; meshes.append((f'{X}_Head_{nm}', R(i, kind)))
    write(X, meshes, os.environ.get('LW_OUT', 'out'))
