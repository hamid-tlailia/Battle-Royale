import sys, json; sys.path.insert(0, '.')
from rig import *
from fit import piece_points
from view import splat
views = []
for i in sys.argv[1:]:
    F = json.load(open(f'{HERE}/fits/fit_{i}.json')); g, P = piece_points(int(i)); c = np.array(F['c'])
    Q = (P - c) * F['s'] + c + np.array(F['t'])
    Bp, _ = lbs(BP, BJ, BW, skin_mats(posed(arm=F['arm'], leg=F.get('leg', 0))))
    views.append((f"{i} s{F['s']:.2f} arm{F['arm']:.0f} leg{F.get('leg', 0):.0f} e{F['err']:.3f}", [(Bp, (170, 170, 220)), (Q[::max(1, len(Q) // 5000)], (220, 30, 30))]))
splat(views, 'f_' + '_'.join(sys.argv[1:]) + '.png', size=360)
