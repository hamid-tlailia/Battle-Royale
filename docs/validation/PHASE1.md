# Phase 1 - LW_BaseCharacter validation (build b2e9c82)

Base: `lw_base.glb` = Mixamo SWAT skeleton (69 bones, T-pose, 30 fingers) carrying all 35 clips. Old soldier rig kept as fallback (`?base=old`).

## Humanoid avatar (?rigcheck=1)
All 10 rigs (lw_base, old soldier, 8 outfits): valid, 52/52 bones mapped, 30/30 fingers, nothing missing. Agent rests in A-pose (51 deg), corrected at runtime.

## Retarget accuracy (?retcheck=1) - 11 clips x 5 phases = 55 samples per bone, 12 bone chains
| Character | Before fix (worst, kind) | After fix (mean / worst) |
|---|---|---|
| Spec Ops (base itself) | 0.0 | 0.0 / 0.0 |
| Marine | 10.3 left hand, persistent | 0.1 / 1.1 |
| Exo | 3.1 | 0.0 / 0.1 |
| Shadow | 13.5 left hand, persistent | 0.1 / 1.1 |
| Hazmat | 10.9 neck->head, persistent | 0.1 / 1.1 |
| Knight | 9.4 left hand + 13.4 neck->head, persistent | 0.1 / 1.1 |
| Archer | (not measured before) | 0.0 / 0.3 |
| Agent (A-pose) | (not measured before) | 0.1 / 1.1 |

The 10-13 deg errors were constant (55/55 samples, standing idle included), not motion artefacts: the hand's rest correction used the first finger in the list instead of the middle finger, and the neck was skipped because SWAT has an extra Neck1 bone. Fixed in `SoldierBody.RestMaps` (AxisChild); `?base=old` keeps its old behaviour.

## Other checks
- 35 clips play on the base (phase1_35clips.png); transitions: 14 changes, 0 bad frames.
- Runtime switching: 30/30 with animator and gun attached; `?base=old` works.
- Console: no script errors (2 shader warnings from the headless test browser only).

## Open (Phase 2 - weapon, not touched)
- Seen from behind, the aim/fire poses point the rifle to the soldier's right with the stock showing at the left (phase1_from_behind.png). The skeleton follows the clips exactly; the gun placement is wrong.
- Pistol uses the old hold; crawl shows the rifle vertical; left hand short of the handguard on short-armed characters.
