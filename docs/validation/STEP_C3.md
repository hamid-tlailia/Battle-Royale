# Right hand on the grip (per-character contact) + Rifle Run - build 497b597

Anchor: the gun's RightHandGrip is placed where each outfit's curled fingers close (halfway between its palm point and its
middle fingertip in the aiming pose, from its own finger bones, in the hand's space). Only gunAtHand changes; gunInHand,
weapon_grips.json and GunFrame unchanged. "Before" = the same build with ?grip=palm (old anchor).

## Full matrix (12 long guns x 8 characters x 5 states, fire from 3 sides = 672 readings)
before 0 failures, after 0 failures (limits: left palm <= 3 cm, extension <= 0.98, slide <= 12 cm, shoulder <= 20 deg)

| gun | before ext / slide / at 20 deg / right | after ext / slide / at 20 deg / right |
|---|---|---|
| rifles, smgs, shotgun (7 guns) | <= 0.96 / 0 / 0 / 1.5 cm | <= 0.96 / 0 / 0 / 0.0 cm |
| shotgun_breacher | 0.98 / 9 / 33 of 56 / 1.5 | 0.97 / 11 / 36 / 0.0 |
| shotgun_long | 0.98 / 11 / 37 / 1.5 | 0.98 / **12 (at the limit)** / 37 / 0.0 |
| sniper | 0.98 / 10 / 35 / 1.5 | 0.97 / 12 / 37 / 0.0 |
| sniper_long | 0.97 / 9 / 29 / 1.5 | 0.97 / 10 / 36 / 0.0 |
| sniper_swift | 0.97 / 7 / 22 / 1.5 | 0.97 / 9 / 24 / 0.0 |
Left palm on its target 0.0 cm at full weight everywhere, before and after.

## Right hand round the grip (gun frame: palm on the grip's right side x <= -0.8 cm, middle fingertip past its left side x >= +1 cm)
| state | before | after |
|---|---|---|
| fire, crouch-fire, idle, Rifle Run, walk-aim (each 8 characters x 12 guns) | 0 / 96 each | 96 / 96 each |
Before: palm point in the middle of the grip (x 0.0) - the hand overlapped the grip, the grip showed outside it. After: palm 1.2-1.6 cm right of the grip centre, fingertips 2.0-3.7 cm left.

## Transitions (lobby trans test, 14 clips incl. idle -> fire -> walk-aim -> Rifle Run -> jump -> idle): 0 bad frames
Right anchor 0.0 cm, left palm 0.0 cm, extension 0.76-0.93 in every clip; crawl (Step F, separate) left palm up to 3.1 cm.

## Rifle Run (mx_rifle_run, replaces mx_sprint with a long gun): barrel 15-33 deg down, hands 0.0 cm; played at 6 / 4.13 x speed (feet keep pace)
