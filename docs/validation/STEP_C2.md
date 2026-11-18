# Phase 2 Step C (A+B) - validation on builds 991832d / 1a2891c / d9ec2c1

Limits (revised, approved): left palm <= 3 cm, arm extension <= 0.98, slide <= 12 cm, shoulder correction <= 20 deg; visual inspection required.

## Shotguns, A and B staged (3 guns x 8 characters x 5 states = 120)
| variant | failures |
|---|---|
| A + B | 0 / 120 |
| A only (?wikB=0) | 1 (shotgun_long walk-aim, ext 0.99) |
| B only (?wikA=0) | 2 (breacher, long walk-aim, ext 0.99) |

## Full matrix (12 long guns x 8 characters x 5 states, fire from 3 sides = 672 readings) - 0 failures
| gun | ext max | slide max | shoulder max | combos at 20 deg | holdBack |
|---|---|---|---|---|---|
| rifles (3) | 0.96 | 0 | 13.2 | 0 | - |
| smgs (3) | 0.96 | 0 | 2.4 | 0 | - |
| shotgun (Benelli M4) | 0.96 | 0 | 7.1 | 0 | - |
| shotgun_breacher | 0.97 | 9 | 20.0 | 33 / 56 | - |
| shotgun_long | 0.98 | 11 | 20.0 | 37 / 56 | 6 cm |
| sniper | 0.98 | 10 | 20.0 | 34 / 56 | - |
| sniper_long | 0.97 | 8 | 20.0 | 29 / 56 | - |
| sniper_swift | 0.97 | 7 | 20.0 | 22 / 56 | - |
Right palm on RightHandGrip <= 1.5 cm everywhere. Flagged for visual inspection (left arm nearly straight): shotgun_breacher, shotgun_long, the three snipers.

## Jump and fire (1a2891c): upper-body aiming layer
Bots firing in the air: layer 1.00 -> barrel vs aim 0 deg (7 readings, rifle / shotgun / smg), left palm 0.0 cm, ext 0.73-0.85. Jumping without firing (layer 0): 18 deg mean, 39 max (old behaviour, intended).
Player input could not be driven in the headless browser (keyboard space / on-screen button not registered at ~1 fps): landing rule to be checked on device.

## Crawl and fire (Step F, tracked separately)
crawl -> FIRE held: still prone aiming pose (mx_prone) -> released: crawl again. Confirmed from the game log.

## Lobby
mx_idle (Mixamo rifle idle) is a combat stance: knees bent, weight on one leg, body turned, rear heel raised on some outfits (Shadow, Agent, Archer, Exo). mx_aim is the same kind of stance. Cause = the clip, not the retarget or the feet placement.
